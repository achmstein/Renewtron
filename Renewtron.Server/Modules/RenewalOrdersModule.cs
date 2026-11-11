using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Modules;

/// <summary>Paid renewals as one record each (see <see cref="RenewalOrder"/>).</summary>
public sealed class RenewalOrdersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/renewal-orders").RequireAuthorization().WithTags("Admin.RenewalOrders");

        // Builds or refreshes the orders from the sale and renewal records. dryRun (the
        // default) only reports counts per stage and reason.
        group.MapPost("/rebuild", async (IRenewalOrderService service, bool dryRun = true, CancellationToken ct = default) =>
            Results.Ok(await service.RebuildAsync(dryRun, ct)));

        // A list of paid renewals. view: to-fix (try again, check first, stuck), decide, or a stage name.
        group.MapGet("/", async (ApplicationDbContext db, string? view = null, string? search = null, int page = 1, int pageSize = 50) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var now = DateTime.UtcNow;
            IQueryable<RenewalOrder> q = db.RenewalOrders.AsNoTracking();
            q = view switch
            {
                "to-fix" => q.OrdersIn([.. WorkQueues.OrderTryAgain, .. WorkQueues.OrderCheckFirst])
                    .Concat(db.RenewalOrders.AsNoTracking().OrdersStuck(db, now)),
                "decide" => q.OrdersIn(WorkQueues.OrderDecide),
                _ when Enum.TryParse<RenewalOrderStage>(view, true, out var stage) => q.Where(o => o.Stage == stage),
                _ => q,
            };
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var digits = new string(term.Where(char.IsDigit).ToArray());
                q = q.Where(o => o.BusinessName.Contains(term) || (digits.Length >= 3 && o.Abn.Contains(digits)));
            }
            var total = await q.CountAsync();
            var items = await Rows(db, q.OrderBy(o => o.DueDate == null).ThenBy(o => o.DueDate).ThenBy(o => o.PaidAt)
                .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
            var byStage = await db.RenewalOrders.AsNoTracking().GroupBy(o => o.Stage)
                .Select(g => new { stage = g.Key.ToString(), count = g.Count(), amount = g.Sum(o => o.AmountPaid) }).ToListAsync();
            return Results.Ok(new { total, page, pageSize, items, byStage });
        });

        // One paid renewal: where it is, what paid for it, and every ASIC run for it.
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var row = await Rows(db, db.RenewalOrders.AsNoTracking().Where(o => o.Id == id)).FirstOrDefaultAsync();
            if (row is null) return Results.NotFound();
            var o = await db.RenewalOrders.AsNoTracking().FirstAsync(x => x.Id == id);
            var client = await ClientRefs.ByIdAsync(db, o.ClientId);
            var attempts = await db.RenewalRequests.AsNoTracking().Where(r => r.RenewalOrderId == id)
                .OrderByDescending(r => r.InitiatedAt)
                .Select(r => new
                {
                    id = r.Id,
                    status = r.Status.ToString(),
                    errorCategory = r.ErrorCategory,
                    failedAtStep = r.FailedAtStep,
                    errorMessage = r.ErrorMessage,
                    initiatedAt = r.InitiatedAt,
                    lastAttemptAt = r.LastAttemptAt,
                    completedAt = r.CompletedAt,
                    nextRetryAt = r.NextRetryAt,
                    attemptCount = r.AttemptCount,
                    paysBy = r.BpayReference != null && r.BpayReference != "" ? "BPAY" : "Card",
                    bpayPaidAt = r.BpayPaidAt,
                    transactionReference = r.TransactionReference,
                    current = r.Id == o.CurrentAttemptId,
                })
                .ToListAsync();
            var source = o.Source switch
            {
                RenewalOrderSource.Ontraport => new { label = "Ontraport sale", to = $"/admin/ontraport-sales/{o.SourceId}" },
                _ => new { label = o.Source == RenewalOrderSource.Wizard ? "Paid on the website" : "Manual renewal", to = $"/admin/renewals/{o.SourceId}" },
            };
            return Results.Ok(new { order = row, client, source, attempts });
        });

        // Try again: retries the current ASIC run, or (when the sale never got that far) puts the
        // sale back for the next processing run. Only for reasons where no money can be at risk.
        group.MapPost("/{id:guid}/retry", async (Guid id, ApplicationDbContext db, IBackgroundJobClient jobs, IRenewalOrderService orders, CancellationToken ct) =>
        {
            var order = await db.RenewalOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order is null) return Results.NotFound();
            if (order.Stage != RenewalOrderStage.NeedsPerson || order.Reason is not { } reason || !WorkQueues.OrderTryAgain.Contains(reason))
                return Results.BadRequest(new { error = "This renewal can't simply be tried again: check it first." });

            string message;
            var attempt = order.CurrentAttemptId is { } aid ? await db.RenewalRequests.FirstOrDefaultAsync(r => r.Id == aid, ct) : null;
            if (attempt is not null && attempt.Status == RenewalStatus.Failed)
            {
                if (attempt.NextRetryAt != null)
                    return Results.BadRequest(new { error = "It is already scheduled to try again." });
                if (RenewalErrorClassifier.MayHoldPayment(attempt.FailedAtStep, attempt.HostedTokenizationId, attempt.ErrorCategory))
                    return Results.BadRequest(new { error = "ASIC may already hold money for it. Check at ASIC first." });
                attempt.Status = RenewalStatus.Pending;
                attempt.HandledAt = null;
                attempt.HandledNote = null;
                if (order.Source == RenewalOrderSource.Ontraport
                    && await db.OntraportSales.FirstOrDefaultAsync(s => s.Id == order.SourceId, ct) is { } sale)
                {
                    sale.Status = OntraportSaleStatus.RenewalQueued;
                    sale.RenewalRequestId = attempt.Id;
                }
                await db.SaveChangesAsync(ct);
                var attemptId = attempt.Id;
                jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(attemptId));
                message = "Trying again now.";
            }
            else if (order.Source == RenewalOrderSource.Ontraport
                && await db.OntraportSales.FirstOrDefaultAsync(s => s.Id == order.SourceId, ct) is { } sale)
            {
                sale.Status = OntraportSaleStatus.Synced;
                sale.RenewalRequestId = null;
                sale.ProcessedAt = null;
                sale.SearchAttempts = 0;
                sale.NextSearchAt = null;
                await db.SaveChangesAsync(ct);
                message = "Back in the queue: the next processing run (07:00) renews it.";
            }
            else
            {
                return Results.BadRequest(new { error = "Nothing to try again for this renewal." });
            }

            await orders.RefreshQuietlyAsync(ct);
            return Results.Ok(new { message });
        });
    }

    /// <summary>The fields lists and pages show for a paid renewal, with the payer's name.</summary>
    private static IQueryable<OrderRow> Rows(ApplicationDbContext db, IQueryable<RenewalOrder> q) => q.Select(o => new OrderRow(
        o.Id, o.Source.ToString(), o.SourceId, o.ClientId,
        db.OntraportSales.Where(s => s.Id == o.SourceId).Select(s => s.ContactName).FirstOrDefault()
            ?? db.RenewalRequests.Where(r => r.Id == o.SourceId).Select(r => r.Lead != null ? r.Lead.FullName : r.Email).FirstOrDefault(),
        db.Clients.Where(c => c.Id == o.ClientId).Select(c => c.FullName).FirstOrDefault(),
        o.BusinessName, o.Abn, o.DueDate, o.Years, o.AmountPaid, o.PaidAt,
        o.Stage.ToString(), o.Reason.HasValue ? o.Reason.Value.ToString() : null, o.Note, o.NextActionAt, o.StageChangedAt,
        o.AttemptCount, o.CurrentAttemptId, o.RenewedAt,
        o.Stage == RenewalOrderStage.NeedsPerson && o.Reason.HasValue && WorkQueues.OrderTryAgain.Contains(o.Reason.Value)));

    public sealed record OrderRow(
        Guid Id, string Source, Guid SourceId, Guid? ClientId, string? PayerName, string? ClientName,
        string BusinessName, string Abn, DateOnly? DueDate, int Years, decimal AmountPaid, DateTime PaidAt,
        string Stage, string? Reason, string? Note, DateTime? NextActionAt, DateTime StageChangedAt,
        int AttemptCount, Guid? CurrentAttemptId, DateTime? RenewedAt, bool CanRetry);
}
