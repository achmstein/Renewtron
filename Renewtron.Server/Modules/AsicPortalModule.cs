using System.Security.Claims;
using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Services.AsicPortal;
using Asic.Integrations.Portal;

namespace Renewtron.Modules;

/// <summary>
/// "Link to our ASIC account": start a run for one business name, see a run's steps, retry it.
/// The run itself is <see cref="AsicPortalLinkService"/> on its own one-worker queue.
/// </summary>
public sealed class AsicPortalModule : ICarterModule
{
    public record StartRequest(string? DeclareAs);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/asic-portal").RequireAuthorization().WithTags("Admin.AsicPortal");

        group.MapPost("/business-names/{nameId:guid}/link", async (Guid nameId, StartRequest? body, IAsicPortalLinkService service, ClaimsPrincipal user) =>
        {
            AsicDeclareAs? declareAs = Enum.TryParse<AsicDeclareAs>(body?.DeclareAs, true, out var d) ? d : null;
            var (run, problem) = await service.QueueAsync(nameId, declareAs, user.Identity?.Name);
            return run is null
                ? Results.Conflict(new { error = problem })
                : Results.Ok(new { id = run.Id, message = $"Linking {run.BusinessName} to our ASIC account: queued." });
        });

        group.MapPost("/{id:guid}/retry", async (Guid id, IAsicPortalLinkService service, ClaimsPrincipal user) =>
        {
            var problem = await service.RetryAsync(id, user.Identity?.Name);
            return problem is null ? Results.Ok(new { message = "Queued again; it carries on from the first step that isn't done." }) : Results.Conflict(new { error = problem });
        });

        group.MapGet("/", async (ApplicationDbContext db, string? status = null, Guid? nameId = null, Guid? clientId = null, int take = 50) =>
        {
            var statuses = Helpers.ParseEnumList<AsicPortalLinkStatus>(status);
            var q = db.AsicPortalLinks.AsNoTracking();
            if (statuses.Length > 0) q = q.Where(r => statuses.Contains(r.Status));
            if (nameId is { } n) q = q.Where(r => r.ClientBusinessNameId == n);
            if (clientId is { } c) q = q.Where(r => r.ClientId == c);
            var rows = await q.OrderByDescending(r => r.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync();
            return Results.Ok(rows.Select(r => ToDto(r)));
        });

        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            if (await db.AsicPortalLinks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id) is not { } run) return Results.NotFound();
            var clientName = run.ClientId is { } cid ? await db.Clients.Where(c => c.Id == cid).Select(c => c.FullName).FirstOrDefaultAsync() : null;
            return Results.Ok(ToDto(run, clientName));
        });
    }

    private static object ToDto(AsicPortalLink r, string? clientName = null) => new
    {
        id = r.Id,
        clientId = r.ClientId,
        clientName,
        clientBusinessNameId = r.ClientBusinessNameId,
        businessName = r.BusinessName,
        abn = r.Abn,
        dateOfBirth = r.DateOfBirth,
        dateOfBirthSource = r.DateOfBirthSource,
        ontraportContactId = r.OntraportContactId,
        declareAs = r.DeclareAs.ToString(),
        status = r.Status.ToString(),
        linkOutcome = r.LinkOutcome,
        emailOutcome = r.EmailOutcome,
        emailReference = r.EmailReference,
        holderOutcome = r.HolderOutcome,
        holderType = r.HolderType,
        holderReference = r.HolderReference,
        repsOutcome = r.RepsOutcome,
        repsRemoved = Lines(r.RepsRemoved),
        repsReferences = Lines(r.RepsReferences),
        failedAtStep = r.FailedAtStep,
        error = r.Error,
        ontraportResult = r.OntraportResult,
        requestedBy = r.RequestedBy,
        attemptCount = r.AttemptCount,
        createdAt = r.CreatedAt,
        startedAt = r.StartedAt,
        finishedAt = r.FinishedAt,
    };

    private static string[] Lines(string? s) => (s ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
