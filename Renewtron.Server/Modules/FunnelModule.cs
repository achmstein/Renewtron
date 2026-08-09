using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

/// <summary>
/// Records how far each visitor gets through the renewal wizard, and reports the drop-off.
/// The browser also fires GA4/Meta events, but those get blocked often enough that the
/// numbers here are the ones to trust.
/// </summary>
public sealed class FunnelModule : ICarterModule
{
    public record TrackRequest(
        string Step,
        string? VisitorId,
        string? SessionId,
        Guid? LeadId,
        string? Abn,
        string? Source,
        string? Detail,
        string? Path,
        string? Referrer);

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/track", async (
            TrackRequest request,
            ApplicationDbContext db,
            HttpContext httpContext,
            ILogger<FunnelModule> logger) =>
        {
            // Never let tracking break the wizard: unknown steps and bad payloads are
            // dropped quietly rather than surfaced to the customer.
            if (!FunnelSteps.IsKnown(request.Step))
                return Results.NoContent();

            var visitorId = Trim(request.VisitorId, 64);
            var sessionId = Trim(request.SessionId, 64);
            if (visitorId is null || sessionId is null)
                return Results.NoContent();

            var (ip, ua) = Helpers.ClientInfo(httpContext);

            try
            {
                db.FunnelEvents.Add(new FunnelEvent
                {
                    Id = Guid.NewGuid(),
                    VisitorId = visitorId,
                    SessionId = sessionId,
                    Step = request.Step,
                    LeadId = request.LeadId,
                    Abn = Trim(Helpers.NormalizeAbn(request.Abn), 20),
                    Source = Trim(request.Source, 64),
                    Detail = Trim(request.Detail, 500),
                    Path = Trim(request.Path, 300),
                    Referrer = Trim(request.Referrer, 500),
                    CreatedAt = DateTime.UtcNow,
                    IpAddress = Trim(ip, 50),
                    UserAgent = Trim(ua, 500),
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record funnel step {Step}", request.Step);
            }

            return Results.NoContent();
        }).WithTags("Wizard");

        var admin = app.MapGroup("/api/admin/funnel").RequireAuthorization().WithTags("Admin.Funnel");

        admin.MapGet("/", async (
            ApplicationDbContext db,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            string? source = null) =>
        {
            var now = DateTime.UtcNow;
            var from = dateFrom?.ToUniversalTime() ?? now.AddDays(-30);
            var to = dateTo?.Date.AddDays(1).ToUniversalTime() ?? now.AddDays(1);

            IQueryable<FunnelEvent> query = db.FunnelEvents.AsNoTracking()
                .Where(e => e.CreatedAt >= from && e.CreatedAt < to);
            if (!string.IsNullOrWhiteSpace(source))
                query = query.Where(e => e.Source == source);

            // One row per visitor per step — everything below is counted off people, not hits.
            var reached = await query
                .GroupBy(e => new { e.VisitorId, e.Step })
                .Select(g => new { g.Key.VisitorId, g.Key.Step, Hits = g.Count() })
                .ToListAsync();

            var visitorsByStep = reached
                .GroupBy(r => r.Step)
                .ToDictionary(g => g.Key, g => g.Count());
            var hitsByStep = reached
                .GroupBy(r => r.Step)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Hits));

            var totalVisitors = reached.Select(r => r.VisitorId).Distinct().Count();

            var steps = FunnelSteps.Ordered.Select((s, i) =>
            {
                var visitors = visitorsByStep.GetValueOrDefault(s.Step, 0);
                var previous = i == 0 ? visitors : visitorsByStep.GetValueOrDefault(FunnelSteps.Ordered[i - 1].Step, 0);
                var entered = visitorsByStep.GetValueOrDefault(FunnelSteps.Ordered[0].Step, 0);
                return new
                {
                    step = s.Step,
                    label = s.Label,
                    visitors,
                    events = hitsByStep.GetValueOrDefault(s.Step, 0),
                    droppedFromPrevious = Math.Max(previous - visitors, 0),
                    stepConversionPct = previous > 0 ? Math.Round((decimal)visitors * 100m / previous, 1) : 0m,
                    overallConversionPct = entered > 0 ? Math.Round((decimal)visitors * 100m / entered, 1) : 0m,
                };
            }).ToList();

            var exits = FunnelSteps.Exits.Select(s => new
            {
                step = s.Step,
                label = s.Label,
                visitors = visitorsByStep.GetValueOrDefault(s.Step, 0),
            }).ToList();

            // The headline number David asked for: where does each person stop?
            var furthestByVisitor = reached
                .GroupBy(r => r.VisitorId)
                .Select(g => g.OrderByDescending(r => FunnelSteps.RankOf(r.Step)).First().Step);
            var stoppedAt = furthestByVisitor
                .GroupBy(s => s)
                .Select(g => new
                {
                    step = g.Key,
                    label = FunnelSteps.LabelFor(g.Key),
                    visitors = g.Count(),
                    rank = FunnelSteps.RankOf(g.Key),
                })
                .OrderBy(s => s.rank)
                .ToList();

            var completedVisitors = visitorsByStep.GetValueOrDefault(FunnelSteps.RenewalComplete, 0);

            // Source breakdown — how well Ontraport links convert against everything else.
            // A visitor who arrives twice from different campaigns counts under each.
            var visitorSources = await query
                .Where(e => e.Source != null)
                .Select(e => new { e.VisitorId, Source = e.Source! })
                .Distinct()
                .ToListAsync();

            var completedVisitorIds = reached
                .Where(r => r.Step == FunnelSteps.RenewalComplete)
                .Select(r => r.VisitorId)
                .ToHashSet();
            var taggedVisitorIds = visitorSources.Select(v => v.VisitorId).ToHashSet();

            var sourceGroups = visitorSources
                .GroupBy(v => v.Source)
                .Select(g => new { Source = g.Key, Visitors = g.Select(v => v.VisitorId).Distinct().ToList() })
                .ToList();

            var directVisitors = reached
                .Select(r => r.VisitorId)
                .Distinct()
                .Where(id => !taggedVisitorIds.Contains(id))
                .ToList();
            if (directVisitors.Count > 0)
                sourceGroups.Add(new { Source = "direct", Visitors = directVisitors });

            var bySource = sourceGroups
                .Select(g => new
                {
                    source = g.Source,
                    visitors = g.Visitors.Count,
                    completed = g.Visitors.Count(completedVisitorIds.Contains),
                    conversionPct = g.Visitors.Count > 0
                        ? Math.Round((decimal)g.Visitors.Count(completedVisitorIds.Contains) * 100m / g.Visitors.Count, 1)
                        : 0m,
                })
                .OrderByDescending(s => s.visitors)
                .ToList();

            var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime();
            var dailyRaw = await db.FunnelEvents.AsNoTracking()
                .Where(e => e.CreatedAt >= fourteenDaysAgo && e.Step == FunnelSteps.AbnViewed)
                .GroupBy(e => new { e.CreatedAt.Year, e.CreatedAt.Month, e.CreatedAt.Day })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Select(e => e.VisitorId).Distinct().Count() })
                .ToListAsync();
            var dailyMap = dailyRaw.ToDictionary(d => new DateOnly(d.Year, d.Month, d.Day), d => d.Count);
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();

            return Results.Ok(new
            {
                totalVisitors,
                completedVisitors,
                conversionPct = totalVisitors > 0 ? Math.Round((decimal)completedVisitors * 100m / totalVisitors, 1) : 0m,
                steps,
                exits,
                stoppedAt,
                bySource,
                daily14d,
            });
        });
    }
}
