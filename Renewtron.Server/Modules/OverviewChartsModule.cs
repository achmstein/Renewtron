using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

/// <summary>
/// The Overview's charts: the business questions David asks, as series. Weeks start on Monday,
/// days and weeks are Sydney's, and test customers never count.
/// </summary>
public sealed class OverviewChartsModule : ICarterModule
{
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");

    public const int Weeks = 26;
    public const int AsicDays = 60;
    public const int AheadWeeks = 12;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/overview/charts", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(Local(now));
            var thisWeek = WeekOf(today);
            var firstWeek = thisWeek.AddDays(-7 * (Weeks - 1));
            var fromUtc = Utc(firstWeek);

            // Paid renewals of the last 26 weeks, as they stand now (test renewals left out).
            var orders = await db.RenewalOrders.AsNoTracking()
                .Where(o => o.PaidAt >= fromUtc)
                .Where(o => !db.RenewalRequests.Any(r => r.Id == o.SourceId && r.IsTest))
                .Select(o => new { o.PaidAt, o.AmountPaid, o.Source, o.Stage, o.Outcome })
                .ToListAsync(ct);

            var weeks = Enumerable.Range(0, Weeks).Select(i => firstWeek.AddDays(7 * i)).Select(w =>
            {
                var of = orders.Where(o => WeekOf(DateOnly.FromDateTime(Local(o.PaidAt))) == w).ToList();
                decimal Sum(Func<RenewalOrderStage, bool> stage) => of.Where(o => stage(o.Stage)).Sum(o => o.AmountPaid);
                return new
                {
                    week = w,
                    direct = of.Where(o => o.Source == RenewalOrderSource.Wizard).Sum(o => o.AmountPaid),
                    ontraport = of.Where(o => o.Source == RenewalOrderSource.Ontraport).Sum(o => o.AmountPaid),
                    manual = of.Where(o => o.Source == RenewalOrderSource.Manual).Sum(o => o.AmountPaid),
                    // What became of that week's money.
                    renewed = Sum(s => s == RenewalOrderStage.Renewed),
                    underWay = Sum(s => s is RenewalOrderStage.Waiting or RenewalOrderStage.Due or RenewalOrderStage.Renewing),
                    needsPerson = Sum(s => s == RenewalOrderStage.NeedsPerson),
                    closed = Sum(s => s == RenewalOrderStage.Closed),
                    refunded = of.Where(o => o.Outcome == RenewalOrderReason.Refunded).Sum(o => o.AmountPaid),
                };
            }).ToList();

            // ASIC, day by day: lodged by card, lodged by BPAY, and runs that ended failed.
            var asicFrom = today.AddDays(-(AsicDays - 1));
            var asicFromUtc = Utc(asicFrom);
            var runs = await db.RenewalRequests.AsNoTracking()
                .Where(r => !r.IsTest && (r.Status == RenewalStatus.Completed || r.Status == RenewalStatus.Failed))
                .Where(r => (r.CompletedAt ?? r.LastAttemptAt ?? r.InitiatedAt) >= asicFromUtc)
                .Select(r => new { r.Status, At = r.CompletedAt ?? r.LastAttemptAt ?? r.InitiatedAt, Bpay = r.BpayReference != null && r.BpayReference != "" })
                .ToListAsync(ct);
            var asic = Enumerable.Range(0, AsicDays).Select(i => asicFrom.AddDays(i)).Select(d =>
            {
                var of = runs.Where(r => DateOnly.FromDateTime(Local(r.At)) == d).ToList();
                return new
                {
                    date = d,
                    card = of.Count(r => r.Status == RenewalStatus.Completed && !r.Bpay),
                    bpay = of.Count(r => r.Status == RenewalStatus.Completed && r.Bpay),
                    failed = of.Count(r => r.Status == RenewalStatus.Failed),
                };
            }).ToList();

            // Clients' names coming due: already paid (an open paid renewal) or not yet.
            var aheadTo = thisWeek.AddDays(7 * AheadWeeks);
            var open = new[] { RenewalOrderStage.Waiting, RenewalOrderStage.Due, RenewalOrderStage.Renewing, RenewalOrderStage.NeedsPerson };
            var due = await db.ClientBusinessNames.AsNoTracking()
                .Where(n => n.Status == ClientBusinessNameStatus.Active && n.RenewalDueDate >= today && n.RenewalDueDate < aheadTo)
                .Select(n => new { Due = n.RenewalDueDate!.Value, Paid = db.RenewalOrders.Any(o => o.ClientBusinessNameId == n.Id && open.Contains(o.Stage)) })
                .ToListAsync(ct);
            var ahead = Enumerable.Range(0, AheadWeeks).Select(i => thisWeek.AddDays(7 * i)).Select(w => new
            {
                week = w,
                paid = due.Count(n => WeekOf(n.Due) == w && n.Paid),
                notPaid = due.Count(n => WeekOf(n.Due) == w && !n.Paid),
            }).ToList();

            // The headline numbers.
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var lastMonthStart = monthStart.AddMonths(-1);
            var lastMonthSameDay = lastMonthStart.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(lastMonthStart.Year, lastMonthStart.Month)));
            var paidThisMonth = await PaidBetween(db, Utc(monthStart), now, ct);
            var paidLastMonthToDate = await PaidBetween(db, Utc(lastMonthStart), Utc(lastMonthSameDay), ct);
            var waiting = await db.RenewalOrders.AsNoTracking().Where(o => o.Stage == RenewalOrderStage.NeedsPerson)
                .GroupBy(_ => 1).Select(g => new { count = g.Count(), amount = g.Sum(o => o.AmountPaid) }).FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                weeks,
                asic,
                ahead,
                headline = new
                {
                    paidThisMonth,
                    paidLastMonthToDate,
                    waitingCount = waiting?.count ?? 0,
                    waitingAmount = waiting?.amount ?? 0,
                },
            });
        }).RequireAuthorization().WithTags("Admin.Overview");
    }

    private static Task<decimal> PaidBetween(ApplicationDbContext db, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        db.RenewalOrders.AsNoTracking()
            .Where(o => o.PaidAt >= fromUtc && o.PaidAt < toUtc)
            .Where(o => !db.RenewalRequests.Any(r => r.Id == o.SourceId && r.IsTest))
            .SumAsync(o => o.AmountPaid, ct);

    private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Sydney);
    private static DateTime Utc(DateOnly sydneyDay) => TimeZoneInfo.ConvertTimeToUtc(sydneyDay.ToDateTime(TimeOnly.MinValue), Sydney);

    /// <summary>The Monday a day's week starts on.</summary>
    public static DateOnly WeekOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
