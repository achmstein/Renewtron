using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Modules;

namespace Renewtron.Services;

public interface IRenewalOrderService
{
    /// <summary>
    /// Builds or refreshes one <see cref="RenewalOrder"/> per paid renewal from the sale and
    /// renewal records, and links every ASIC run to its order. With <paramref name="dryRun"/> it
    /// only reports what it would write.
    /// </summary>
    Task<RenewalOrderRebuildReport> RebuildAsync(bool dryRun, CancellationToken ct);

    /// <summary>The scheduled refresh.</summary>
    Task RefreshAsync(CancellationToken ct);

    /// <summary>Refresh right after a person's action, so pages show the new status at once. Never throws.</summary>
    Task RefreshQuietlyAsync(CancellationToken ct);
}

public sealed record RenewalOrderRebuildReport(
    int Orders, int Created, int StageChanged,
    int AttemptsLinked, int OrphanAttemptsAttached, int AttemptsWithoutOrder,
    int OpenDuplicates,
    IReadOnlyList<RenewalOrderRebuildReport.Bucket> ByStage)
{
    public sealed record Bucket(string Stage, string? Reason, int Count, decimal Amount);
}

/// <summary>
/// Phase 1–2 of the renewal status refactor: paid renewals as one record each, computed from what
/// the pipeline stores today by <see cref="RenewalOrderProjection"/>. Readers move to orders next;
/// later the pipeline writes orders directly.
/// </summary>
public class RenewalOrderService(ApplicationDbContext db, ILogger<RenewalOrderService> logger) : IRenewalOrderService
{
    public const string RecurringJobId = "renewal-orders-refresh";

    /// <summary>
    /// Money arrived (a sale synced, a wizard or manual payment): write its order now
    /// rather than at the next 10-minute refresh, so it is a record from the moment it is paid.
    /// </summary>
    public static void EnqueueRefresh(IBackgroundJobClient jobs) =>
        jobs.Enqueue<IRenewalOrderService>(s => s.RefreshAsync(CancellationToken.None));

    private sealed record Shell(
        RenewalOrderSource Source, Guid SourceId, Guid? ClientId, Guid? NameId, string Abn, string BusinessName,
        DateOnly? DueDate, int Years, decimal Amount, DateTime PaidAt, RenewalOrderProjection.Source State);

    private sealed record Run(
        Guid Id, RenewalSource Source, PaymentType PaymentType, string? StripeStatus, string Abn, string BusinessName,
        Guid? NameId, Guid? ClientId, int Years, decimal Amount, RenewalOrderProjection.Attempt Attempt);

    public Task RefreshAsync(CancellationToken ct) => RebuildAsync(false, ct);

    public async Task RefreshQuietlyAsync(CancellationToken ct)
    {
        try
        {
            db.ChangeTracker.Clear();
            await RebuildAsync(false, ct);
        }
        catch (Exception ex)
        {
            // The scheduled refresh catches up within 10 minutes (e.g. it ran at the same moment).
            logger.LogWarning(ex, "Renewal orders: refresh after an action failed");
        }
    }

    public async Task<RenewalOrderRebuildReport> RebuildAsync(bool dryRun, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var runs = await db.RenewalRequests.AsNoTracking().Where(r => !r.IsTest)
            .Select(r => new
            {
                r.Id, r.Source, r.PaymentType, Stripe = r.StripePayment != null ? r.StripePayment.PaymentStatus : null,
                Abn = r.SearchResult.SearchLog.Abn, Name = r.SearchResult.BusinessName, r.ClientBusinessNameId, r.ClientId,
                r.RenewalYears, r.Amount, r.Status, r.InitiatedAt, r.ErrorCategory, r.ErrorMessage, r.CompletedAt,
                r.NextRetryAt, r.HandledAt, r.HandledNote, r.RenewalOrderId,
            })
            .ToListAsync(ct);
        var runById = runs.ToDictionary(r => r.Id, r => new Run(
            r.Id, r.Source, r.PaymentType, r.Stripe, Helpers.NormalizeAbn(r.Abn), r.Name, r.ClientBusinessNameId, r.ClientId,
            r.RenewalYears, r.Amount,
            new RenewalOrderProjection.Attempt(r.Id, r.Status, r.InitiatedAt, r.ErrorCategory, r.ErrorMessage, r.CompletedAt,
                r.NextRetryAt, r.HandledAt, r.HandledNote)));
        var currentLink = runs.ToDictionary(r => r.Id, r => r.RenewalOrderId);

        var shells = new List<Shell>();
        var linked = new Dictionary<(RenewalOrderSource, Guid), Guid>();

        var sales = await db.OntraportSales.AsNoTracking()
            .Select(s => new
            {
                s.Id, s.ClientId, s.ClientBusinessNameId, s.Abn, s.BusinessName, s.RenewalDueDate, s.RenewalYears, s.AmountPaid,
                s.SyncedAt, s.Status, s.ErrorMessage, s.HandledAt, s.HandledNote, s.RenewalRequestId, s.NextSearchAt,
            })
            .ToListAsync(ct);
        foreach (var s in sales)
        {
            var due = s.RenewalDueDate is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
            // A completed sale's due date is usually rolled forward by the term it bought (not always):
            // the order is for the date that was renewed.
            if (due is { } saleDue) due = SaleDueDates.Renewed(s.Status, saleDue, s.SyncedAt, s.RenewalYears);
            var state = new RenewalOrderProjection.Source(RenewalOrderProjection.FromSale(s.Status), due, s.ErrorMessage,
                s.HandledAt, s.HandledNote, s.Status == OntraportSaleStatus.AsicNotYetDue, s.RenewalRequestId, s.NextSearchAt);
            shells.Add(new Shell(RenewalOrderSource.Ontraport, s.Id, s.ClientId, s.ClientBusinessNameId, Helpers.NormalizeAbn(s.Abn),
                s.BusinessName, due, s.RenewalYears, s.AmountPaid, s.SyncedAt, state));
            if (s.RenewalRequestId is { } rid) linked[(RenewalOrderSource.Ontraport, s.Id)] = rid;
        }

        // Wizard and manual renewals are their own paying record: one order each, if the client paid.
        var nameDue = await db.ClientBusinessNames.AsNoTracking().Where(n => n.RenewalDueDate != null)
            .Select(n => new { n.Id, n.RenewalDueDate }).ToDictionaryAsync(n => n.Id, n => n.RenewalDueDate, ct);
        var linkedRunIds = linked.Values.ToHashSet();
        foreach (var r in runById.Values.Where(r => r.Source == RenewalSource.Renewtron && !linkedRunIds.Contains(r.Id)))
        {
            var paid = r.PaymentType == PaymentType.External || r.StripeStatus == StripePaymentStatuses.Succeeded;
            if (!paid) continue;
            var source = r.PaymentType == PaymentType.Stripe ? RenewalOrderSource.Wizard : RenewalOrderSource.Manual;
            var due = r.Attempt.Status != RenewalStatus.Completed && r.NameId is { } nid && nameDue.TryGetValue(nid, out var nd) ? nd : null;
            var state = new RenewalOrderProjection.Source(RenewalOrderProjection.SourceState.Queued, due, LinkedAttemptId: r.Id);
            shells.Add(new Shell(source, r.Id, r.ClientId, r.NameId, r.Abn, r.BusinessName, due, r.Years, r.Amount,
                r.Attempt.InitiatedAt, state));
            linked[(source, r.Id)] = r.Id;
        }

        // Every run belongs to one order: its own link first, then (for runs a sale let
        // go of) the order from the same source for the same name, paid most recently before the run.
        var attemptsOf = shells.ToDictionary(s => (s.Source, s.SourceId), _ => new List<RenewalOrderProjection.Attempt>());
        var orderOfRun = new Dictionary<Guid, (RenewalOrderSource, Guid)>();
        foreach (var (key, rid) in linked)
            if (runById.TryGetValue(rid, out var run)) { attemptsOf[key].Add(run.Attempt); orderOfRun[rid] = key; }
        var byName = shells.GroupBy(s => (s.Source, s.Abn, Key: BusinessNameText.MatchKey(s.BusinessName)))
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.PaidAt).ToList());
        int orphansAttached = 0, unmatched = 0;
        foreach (var run in runById.Values.Where(r => !orderOfRun.ContainsKey(r.Id)))
        {
            var source = run.Source switch
            {
                RenewalSource.Ontraport => RenewalOrderSource.Ontraport,
                _ => (RenewalOrderSource?)null,
            };
            if (source is null || !byName.TryGetValue((source.Value, run.Abn, BusinessNameText.MatchKey(run.BusinessName)), out var candidates))
            {
                unmatched++;
                continue;
            }
            var owner = candidates.LastOrDefault(c => c.PaidAt <= run.Attempt.InitiatedAt.AddDays(1)) ?? candidates[0];
            var key = (owner.Source, owner.SourceId);
            attemptsOf[key].Add(run.Attempt);
            orderOfRun[run.Id] = key;
            orphansAttached++;
        }

        // Compute and write. A stored outcome (a person's or the ASIC check's) goes into the projection.
        var existing = await db.RenewalOrders.ToDictionaryAsync(o => (o.Source, o.SourceId), ct);
        int created = 0, changed = 0;
        var computed = new List<RenewalOrder>(shells.Count);
        foreach (var s in shells)
        {
            var attempts = attemptsOf[(s.Source, s.SourceId)];
            var state = existing.TryGetValue((s.Source, s.SourceId), out var stored) && stored.Outcome != null
                ? s.State with { Outcome = stored.Outcome, OutcomeAt = stored.OutcomeAt, Evidence = stored.Evidence }
                : s.State;
            var result = RenewalOrderProjection.Compute(state, attempts, today);
            if (!existing.TryGetValue((s.Source, s.SourceId), out var order))
            {
                order = new RenewalOrder { Id = Guid.NewGuid(), Source = s.Source, SourceId = s.SourceId, CreatedAt = now, StageChangedAt = now };
                if (!dryRun) db.RenewalOrders.Add(order);
                created++;
            }
            else if (order.Stage != result.Stage || order.Reason != result.Reason)
            {
                order.StageChangedAt = now;
                changed++;
            }
            order.ClientId = s.ClientId;
            order.ClientBusinessNameId = s.NameId;
            order.Abn = s.Abn;
            order.BusinessName = s.BusinessName.Length > 200 ? s.BusinessName[..200] : s.BusinessName;
            var nameKey = BusinessNameText.MatchKey(s.BusinessName);
            order.NameKey = nameKey.Length > 200 ? nameKey[..200] : nameKey;
            order.DueDate = s.DueDate;
            order.Years = s.Years;
            order.AmountPaid = s.Amount;
            order.PaidAt = s.PaidAt;
            order.Stage = result.Stage;
            order.Reason = result.Reason;
            order.NextActionAt = result.NextActionAt;
            order.Note = result.Note is { Length: > 1000 } n ? n[..1000] : result.Note;
            order.CurrentAttemptId = result.CurrentAttemptId;
            order.AttemptCount = attempts.Count;
            order.RenewedAt = result.RenewedAt;
            order.UpdatedAt = now;
            computed.Add(order);
        }

        var duplicates = computed
            .Where(o => o.Stage is not (RenewalOrderStage.Closed or RenewalOrderStage.Renewed) && o.DueDate != null)
            .GroupBy(o => (o.Abn, o.NameKey, o.DueDate))
            .Sum(g => g.Count() - 1);

        var relink = new List<(Guid RunId, Guid OrderId)>();
        if (!dryRun)
        {
            await db.SaveChangesAsync(ct);
            var idOf = computed.ToDictionary(o => (o.Source, o.SourceId), o => o.Id);
            foreach (var (runId, key) in orderOfRun)
                if (idOf.TryGetValue(key, out var orderId) && currentLink[runId] != orderId)
                    relink.Add((runId, orderId));
            foreach (var group in relink.GroupBy(x => x.OrderId))
            {
                var ids = group.Select(x => x.RunId).ToList();
                foreach (var chunk in ids.Chunk(500))
                    await db.RenewalRequests.Where(r => chunk.Contains(r.Id))
                        .ExecuteUpdateAsync(u => u.SetProperty(r => r.RenewalOrderId, group.Key), ct);
            }
        }

        var report = new RenewalOrderRebuildReport(
            computed.Count, created, changed,
            orderOfRun.Count - orphansAttached, orphansAttached, unmatched, duplicates,
            computed.GroupBy(o => (o.Stage, o.Reason))
                .Select(g => new RenewalOrderRebuildReport.Bucket(g.Key.Stage.ToString(), g.Key.Reason?.ToString(), g.Count(), g.Sum(o => o.AmountPaid)))
                .OrderBy(b => b.Stage).ThenBy(b => b.Reason)
                .ToList());
        logger.LogInformation("Renewal orders {Mode}: {Orders} orders, {Created} new, {Changed} changed stage, {Relinked} runs relinked",
            dryRun ? "dry run" : "rebuilt", report.Orders, created, changed, relink.Count);
        return report;
    }
}
