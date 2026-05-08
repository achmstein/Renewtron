using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Modules;

public sealed class WinBackModule : ICarterModule
{
    public record PreviewRequest(
        string? Outcome,
        bool? ReminderOptIn,
        string? Search,
        DateTime? CreatedFrom,
        DateTime? CreatedTo);

    public record SendRequest(
        string? Outcome,
        bool? ReminderOptIn,
        string? Search,
        DateTime? CreatedFrom,
        DateTime? CreatedTo);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/win-back").RequireAuthorization().WithTags("Admin.WinBack");

        group.MapPost("/preview", async (PreviewRequest body, IWinBackService svc, ApplicationDbContext db) =>
        {
            // Cheap avg-basket calculation (same shape as DashboardModule's basketStats).
            var basketStats = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Status == RenewalStatus.Completed && r.Amount > 0)
                .GroupBy(r => 1)
                .Select(g => new { Count = g.Count(), Sum = g.Sum(r => r.Amount) })
                .FirstOrDefaultAsync();
            var avgBasket = basketStats != null && basketStats.Count > 0 ? basketStats.Sum / basketStats.Count : 79m;

            var preview = await svc.PreviewAsync(new WinBackFilter
            {
                Outcome = body.Outcome,
                ReminderOptIn = body.ReminderOptIn,
                Search = body.Search,
                CreatedFrom = body.CreatedFrom,
                CreatedTo = body.CreatedTo,
            }, avgBasket);

            return Results.Ok(new
            {
                recipientCount = preview.RecipientCount,
                recoverableValue = preview.RecoverableValue,
                sampleNames = preview.SampleNames,
                subject = preview.Subject,
                bodyPreview = preview.BodyPreview,
                avgBasket,
            });
        });

        group.MapPost("/send", async (SendRequest body, IWinBackService svc) =>
        {
            var result = await svc.SendAsync(new WinBackFilter
            {
                Outcome = body.Outcome,
                ReminderOptIn = body.ReminderOptIn,
                Search = body.Search,
                CreatedFrom = body.CreatedFrom,
                CreatedTo = body.CreatedTo,
            });

            return Results.Ok(new { enqueued = result.Enqueued, batchId = result.BatchId });
        });
    }
}
