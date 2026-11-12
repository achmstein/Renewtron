using System.Security.Claims;
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

        // A list of paid renewals. view: to-fix (try again, check first, stuck), decide, a stage name,
        // or a reason name (TooLate, NoDueDate, BelowMinimumFee…: the renewals stopped for that reason).
        // source: Ontraport, Wizard (paid on the website: "Direct") or Manual, comma-separated, on top of any view.
        group.MapGet("/", async (ApplicationDbContext db, string? view = null, string? search = null, string? source = null, int page = 1, int pageSize = 50) =>
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
                _ when Enum.TryParse<RenewalOrderStage>(view, true, out var stage) && !int.TryParse(view, out _) => q.Where(o => o.Stage == stage),
                _ when Enum.TryParse<RenewalOrderReason>(view, true, out var reason) && !int.TryParse(view, out _) => q.Where(o => o.Reason == reason),
                _ => q,
            };
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var digits = new string(term.Where(char.IsDigit).ToArray());
                q = q.Where(o => o.BusinessName.Contains(term) || (digits.Length >= 3 && o.Abn.Contains(digits)));
            }
            // Where they paid, as facets: the counts are for this view and search, before the source is picked.
            var bySource = (await q.GroupBy(o => o.Source).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
                .Select(s => new { value = s.Key.ToString(), count = s.Count });
            var sources = (source ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<RenewalOrderSource>(s, true, out var v) && !int.TryParse(s, out _) ? v : (RenewalOrderSource?)null)
                .OfType<RenewalOrderSource>().ToList();
            if (sources.Count > 0) q = q.Where(o => sources.Contains(o.Source));
            var total = await q.CountAsync();
            // All renewals: the latest paid first. A queue or stage: the soonest due first, as it's worked.
            var ordered = string.IsNullOrEmpty(view)
                ? q.OrderByDescending(o => o.PaidAt).ThenByDescending(o => o.Id)
                : q.OrderBy(o => o.DueDate == null).ThenBy(o => o.DueDate).ThenBy(o => o.PaidAt);
            var items = await Rows(db, ordered.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
            var byStage = await db.RenewalOrders.AsNoTracking().GroupBy(o => o.Stage)
                .Select(g => new { stage = g.Key.ToString(), count = g.Count(), amount = g.Sum(o => o.AmountPaid) }).ToListAsync();
            // What's stopped for a person, by why: the tabs' counts and their guides' totals.
            var byReason = await db.RenewalOrders.AsNoTracking().Where(o => o.Stage == RenewalOrderStage.NeedsPerson && o.Reason != null)
                .GroupBy(o => o.Reason!.Value)
                .Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(o => o.AmountPaid) }).ToListAsync();
            return Results.Ok(new
            {
                total, page, pageSize, items, byStage, bySource,
                byReason = byReason.Select(r => new { reason = r.Key.ToString(), count = r.Count, amount = r.Amount }),
            });
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
            var decision = new
            {
                outcome = o.Outcome?.ToString(),
                at = o.OutcomeAt,
                by = o.OutcomeBy,
                evidence = o.Evidence,
                evidenceSource = o.EvidenceSource?.ToString(),
                evidenceAt = o.EvidenceAt,
                reference = o.OutcomeReference,
                asicCheckedAt = o.AsicCheckedAt,
            };
            // The website visit that led to it (wizard payments only; Ontraport sales come from Ontraport).
            var visit = await db.RenewalRequests.AsNoTracking().Where(r => r.Id == o.SourceId && r.LeadId != null)
                .Select(r => new { id = r.LeadId, at = r.Lead!.CreatedAt }).FirstOrDefaultAsync();
            return Results.Ok(new { order = row, client, source, attempts, decision, visit, canFixName = o.Source == RenewalOrderSource.Ontraport });
        });

        // Renewtron's own ASIC check over every unfinished order. dryRun (the default) only reports.
        group.MapPost("/asic-check", async (IAsicCheckService check, bool dryRun = true, CancellationToken ct = default) =>
            Results.Ok(await check.CheckAllAsync(dryRun, ct)));

        // Asks ASIC about one order now and records what it says.
        group.MapPost("/{id:guid}/check-asic", async (Guid id, IAsicCheckService check, CancellationToken ct) =>
            await check.CheckOneAsync(id, ct) is { } item ? Results.Ok(item) : Results.BadRequest(new { error = "This renewal has no due date or ABN to check." }));

        // The payments behind a paid renewal that a person could refund (nothing is refunded here).
        group.MapGet("/{id:guid}/refund", async (Guid id, IOrderRefundService refunds, CancellationToken ct) =>
            await refunds.OptionsAsync(id, ct) is { } options ? Results.Ok(options) : Results.NotFound());

        // Refunds the one payment a person picked, to the customer's card, through where they paid.
        group.MapPost("/{id:guid}/refund", async (Guid id, RefundRequest body, ClaimsPrincipal user, IOrderRefundService refunds, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Payment)) return Results.BadRequest(new { error = "Pick the payment to refund." });
            var by = user.Identity?.Name ?? "Staff";
            var outcome = await refunds.RefundAsync(id, body.Payment, by, body.Note, ct);
            return outcome.Ok
                ? Results.Ok(new { message = outcome.Message, reference = outcome.Reference })
                : Results.Conflict(new { error = outcome.Message });
        });

        // A person's decision on how the order ends (or, for a lapsed name, what happens next), with
        // the note and reference it rests on.
        group.MapPost("/{id:guid}/outcome", async (Guid id, OutcomeRequest body, ClaimsPrincipal user, ApplicationDbContext db, IRenewalOrderService orders, CancellationToken ct) =>
        {
            var order = await db.RenewalOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order is null) return Results.NotFound();
            var result = Enum.TryParse<RenewalOrderReason>(body.Outcome, out var outcome)
                ? RenewalOrderActions.Decide(order, outcome, body.Note, body.Reference, By(user))
                : OrderActionResult.No("Choose renewed elsewhere, refunded, register again, client cancelled, disputed or duplicate.");
            return await Finish(result, db, orders, ct);
        });

        // Undoes a decision: the pipeline decides the status again.
        group.MapPost("/{id:guid}/reopen", async (Guid id, ApplicationDbContext db, IRenewalOrderService orders, CancellationToken ct) =>
            await db.RenewalOrders.FirstOrDefaultAsync(o => o.Id == id, ct) is { } order
                ? await Finish(RenewalOrderActions.Reopen(order), db, orders, ct)
                : Results.NotFound());

        // The name we hold doesn't match ASIC's: corrects it on the sale and puts it back in the queue.
        group.MapPost("/{id:guid}/fix-name", async (Guid id, FixNameRequest body, ApplicationDbContext db, IRenewalOrderService orders, CancellationToken ct) =>
            await db.RenewalOrders.FirstOrDefaultAsync(o => o.Id == id, ct) is { } order
                ? await Finish(await RenewalOrderActions.FixNameAsync(db, order, body.BusinessName, ct), db, orders, ct)
                : Results.NotFound());

        // A due date a person found (at ASIC, or from the client) for a sale that came without one.
        group.MapPost("/{id:guid}/due-date", async (Guid id, DueDateRequest body, ApplicationDbContext db, IRenewalOrderService orders, CancellationToken ct) =>
            await db.RenewalOrders.FirstOrDefaultAsync(o => o.Id == id, ct) is { } order
                ? await Finish(await RenewalOrderActions.SetDueDateAsync(db, order, body.DueDate, DateOnly.FromDateTime(DateTime.UtcNow), ct), db, orders, ct)
                : Results.NotFound());

        // Try again: retries the current ASIC run, or puts the sale back for the next processing run.
        group.MapPost("/{id:guid}/retry", async (Guid id, ApplicationDbContext db, IBackgroundJobClient jobs, IRenewalOrderService orders, CancellationToken ct) =>
            await db.RenewalOrders.FirstOrDefaultAsync(o => o.Id == id, ct) is { } order
                ? await Finish(await RenewalOrderActions.RetryAsync(db, jobs, order, ct), db, orders, ct)
                : Results.NotFound());

        // One action on many renewals at once (the list's bulk bar): each gets the same checks as on
        // its own; the ones it can't apply to are listed with why. The orders refresh once, after all.
        // Refunds are not here: each is a payment a person picks.
        group.MapPost("/bulk", async (BulkRequest body, ClaimsPrincipal user, ApplicationDbContext db, IBackgroundJobClient jobs,
            IAsicCheckService check, Renewtron.Services.AsicRegistry.IAsicDueDateService dueDates, IRenewalOrderService orders, CancellationToken ct) =>
        {
            var ids = (body.Ids ?? []).Distinct().ToList();
            if (ids.Count == 0) return Results.BadRequest(new { error = "Pick the renewals first." });
            if (ids.Count > MaxBulk) return Results.BadRequest(new { error = $"Up to {MaxBulk} at a time." });
            if (body.Action is "check" or "find-due" && ids.Count > MaxBulkCheck)
                return Results.BadRequest(new { error = $"ASIC is asked one name at a time: up to {MaxBulkCheck} at once." });
            if (body.Action == "find-due" && !dueDates.IsEnabled)
                return Results.BadRequest(new { error = "ASIC due-date lookups are switched off in Settings." });
            // One ASIC register search per ABN, however many of its names were ticked.
            var asicByAbn = new Dictionary<string, IReadOnlyDictionary<string, Renewtron.Services.AsicRegistry.AsicNameDue>?>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var list = await db.RenewalOrders.Where(o => ids.Contains(o.Id)).ToListAsync(ct);
            var done = 0;
            var skipped = new List<object>();
            foreach (var order in list)
            {
                OrderActionResult result;
                switch (body.Action)
                {
                    case "retry":
                        result = await RenewalOrderActions.RetryAsync(db, jobs, order, ct);
                        break;
                    case "reopen":
                        result = RenewalOrderActions.Reopen(order);
                        break;
                    case "check":
                        await db.SaveChangesAsync(ct);
                        var item = await check.CheckOneAsync(order.Id, ct, refresh: false);
                        result = item is null ? OrderActionResult.No("No due date or ABN to check.") : OrderActionResult.Done(item.Evidence);
                        break;
                    case "find-due":
                        if (!asicByAbn.TryGetValue(order.Abn, out var found))
                        {
                            found = await dueDates.LookupAsync(order.Abn, ct);
                            if (found is not null) await dueDates.ApplyAsync(order.Abn, found, DateTime.UtcNow, ct);
                            asicByAbn[order.Abn] = found;
                        }
                        result = found is null
                            ? OrderActionResult.No("ASIC didn't answer. Try again later.")
                            : !found.TryGetValue(BusinessNameText.MatchKey(order.BusinessName), out var name)
                                ? OrderActionResult.No("ASIC doesn't list this name under the ABN: correct the name, or refund.")
                                : name.RenewalDate is not { } asicDue
                                    ? OrderActionResult.No($"ASIC lists it ({name.Status ?? "no status"}) but shows no renewal date.")
                                    : await RenewalOrderActions.SetDueDateAsync(db, order, asicDue, today, ct);
                        break;
                    case "outcome" when Enum.TryParse<RenewalOrderReason>(body.Outcome, out var outcome):
                        result = RenewalOrderActions.Decide(order, outcome, body.Note, body.Reference, By(user));
                        break;
                    default:
                        return Results.BadRequest(new { error = "Unknown action." });
                }
                if (result.Ok) done++;
                else skipped.Add(new { id = order.Id, businessName = order.BusinessName, why = result.Message });
            }
            await db.SaveChangesAsync(ct);
            await orders.RefreshQuietlyAsync(ct);
            var message = skipped.Count == 0 ? $"Done for {Plural(done)}." : $"Done for {Plural(done)}; {skipped.Count} skipped.";
            return Results.Ok(new { message, done, skipped });
        });
    }

    public const int MaxBulk = 200;
    public const int MaxBulkCheck = 25;

    private static string By(ClaimsPrincipal user) => user.Identity?.Name ?? "admin";
    private static string Plural(int n) => n == 1 ? "1 renewal" : $"{n} renewals";

    /// <summary>Saves and refreshes after an action that went through; says why when it didn't.</summary>
    private static async Task<IResult> Finish(OrderActionResult result, ApplicationDbContext db, IRenewalOrderService orders, CancellationToken ct)
    {
        if (!result.Ok) return Results.BadRequest(new { error = result.Message });
        await db.SaveChangesAsync(ct);
        await orders.RefreshQuietlyAsync(ct);
        return Results.Ok(new { message = result.Message });
    }

    public sealed record OutcomeRequest(string Outcome, string? Note, string? Reference);
    public sealed record FixNameRequest(string BusinessName);
    public sealed record RefundRequest(string Payment, string? Note);
    public sealed record DueDateRequest(DateOnly DueDate);
    /// <param name="Action">retry, check (ask ASIC), find-due (read the due date from ASIC's register), outcome (with Outcome and Note), or reopen.</param>
    public sealed record BulkRequest(List<Guid>? Ids, string Action, string? Outcome, string? Note, string? Reference);


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
