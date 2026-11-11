using Asic.Integrations.Abstractions;
using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// The admin's home page: every queue that needs a person, in one response, grouped by job and
/// ordered by risk. Money we have taken and not delivered first, then ASIC's BPAY bills, then
/// ASIC keys, then decisions (possible duplicate clients). Each item carries its client so the
/// page can name them and link to their record; the page writes the sentences.
/// </summary>
public sealed class TodayModule : ICarterModule
{
    /// <summary>Rows sent per group; the group's count and total still cover all of it.</summary>
    private const int RowsPerGroup = 15;

    /// <summary>
    /// One thing that needs a person. <c>Reason</c> is the record's own status or error category
    /// (renewal: ErrorCategory or "Stuck"; sale: OntraportSaleStatus; BPAY: Airwallex status or
    /// "TO_PAY"; key letter: AsicKeyNotificationStatus), which the page turns into words.
    /// </summary>
    public sealed record TodayItem(
        string Kind,
        string Id,
        string Reason,
        Guid? ClientId,
        string? ClientName,
        string? BusinessName,
        string? Abn,
        decimal? Amount,
        int? Years,
        DateTime At,
        DateOnly? DueAt,
        string? Detail,
        string? FailedAtStep,
        bool CanRetry,
        int? Count = null,
        Guid? OtherClientId = null,
        string? OtherClientName = null);

    public sealed record TodayGroup(string Key, int Count, decimal? Total, IReadOnlyList<TodayItem> Items);

    public sealed record Counts(
        int TryAgain, int CheckFirst, int Decide, int Stuck, int BpayToPay, int KeyRequests, int KeysToSort, int Duplicates)
    {
        /// <summary>Paid renewals that need a person to get them renewed ("Renewals to fix").</summary>
        public int RenewalsToFix => TryAgain + CheckFirst + Stuck;
        public int Total => TryAgain + CheckFirst + Decide + Stuck + BpayToPay + KeyRequests + KeysToSort + Duplicates;
    }

    private static IQueryable<RenewalOrder> Orders(ApplicationDbContext db) => db.RenewalOrders.AsNoTracking();

    public static async Task<Counts> CountAsync(ApplicationDbContext db, DateTime now) => new(
        TryAgain: await Orders(db).OrdersIn(WorkQueues.OrderTryAgain).CountAsync(),
        CheckFirst: await Orders(db).OrdersIn(WorkQueues.OrderCheckFirst).CountAsync(),
        Decide: await Orders(db).OrdersIn(WorkQueues.OrderDecide).CountAsync(),
        Stuck: await Orders(db).OrdersStuck(db, now).CountAsync(),
        BpayToPay: await db.RenewalRequests.AsNoTracking().Where(WorkQueues.BpayToPay).CountAsync(),
        KeyRequests: await db.AsicKeyRequests.AsNoTracking()
            .CountAsync(r => !r.IsTest && WorkQueues.KeyRequestOpen.Contains(r.Status)),
        KeysToSort: await db.AsicKeyNotifications.AsNoTracking()
            .CountAsync(n => !WorkQueues.KeyLetterSettled.Contains(n.Status)),
        Duplicates: await db.Clients.AsNoTracking()
            .CountAsync(c => !c.IsTest && c.PossibleDuplicateOfId != null));

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/today", async (ApplicationDbContext db, IOptionsSnapshot<AsicSettings> asic, ISmsProvider sms,
            ILogger<TodayModule> logger, Guid? clientId) =>
        {
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);

            // ?clientId= narrows every queue to one client, for the client page's "Needs attention":
            // all of their items, and their key requests one per line.
            var forClient = clientId is not null;
            var clientAbns = clientId is { } cid
                ? (await db.ClientAbns.AsNoTracking().Where(a => a.ClientId == cid).Select(a => a.Abn).ToListAsync()).ToHashSet()
                : [];
            bool Mine(Guid? owner) => !forClient || owner == clientId;
            var rowsPerGroup = forClient ? int.MaxValue : RowsPerGroup;

            // ── Paid renewals that need a person (one record each: RenewalOrder) ──
            async Task<List<TodayItem>> OrderItems(IQueryable<RenewalOrder> q, bool canRetry, string? reason = null)
            {
                var rows = await q.Where(o => !forClient || o.ClientId == clientId)
                    .Select(o => new
                    {
                        o.Id, o.ClientId, o.BusinessName, o.Abn, o.AmountPaid, o.Years, o.StageChangedAt, o.DueDate,
                        o.Note, Reason = o.Reason,
                        Who = db.OntraportSales.Where(s => s.Id == o.SourceId).Select(s => s.ContactName).FirstOrDefault()
                            ?? db.RenewalRequests.Where(r => r.Id == o.SourceId).Select(r => r.Lead != null ? r.Lead.FullName : r.Email).FirstOrDefault(),
                    })
                    .ToListAsync();
                return rows.Select(o => new TodayItem(
                    "order", o.Id.ToString(), reason ?? o.Reason?.ToString() ?? "", o.ClientId, o.Who, o.BusinessName, o.Abn,
                    o.AmountPaid, o.Years, o.StageChangedAt, o.DueDate, o.Note, null, CanRetry: canRetry)).ToList();
            }
            // Riskiest first: names due soonest (or just past due), then those with no due date,
            // then names long past due; oldest first within each.
            List<TodayItem> Riskiest(IEnumerable<TodayItem> items) => items
                .OrderBy(i => i.DueAt is null ? 1 : i.DueAt >= today.AddDays(-30) ? 0 : 2)
                .ThenBy(i => i.DueAt)
                .ThenBy(i => i.At)
                .ToList();

            var tryAgain = Riskiest((await OrderItems(Orders(db).OrdersStuck(db, now), canRetry: false, reason: "Stuck"))
                .Concat(await OrderItems(Orders(db).OrdersIn(WorkQueues.OrderTryAgain), canRetry: true)));
            var checkFirst = Riskiest(await OrderItems(Orders(db).OrdersIn(WorkQueues.OrderCheckFirst), canRetry: false));
            var decideMoney = Riskiest(await OrderItems(Orders(db).OrdersIn(WorkQueues.OrderDecide), canRetry: false));

            // ── ASIC BPAY bills ───────────────────────────────────────────────────
            var bills = await db.RenewalRequests.AsNoTracking().Where(WorkQueues.BpayToPay)
                .OrderBy(r => r.CompletedAt ?? r.LastAttemptAt ?? r.InitiatedAt)
                .Select(r => new
                {
                    r.Id, r.ClientId, LeadName = r.Lead != null ? r.Lead.FullName : null, r.Email,
                    BusinessName = r.SearchResult.BusinessName, Abn = r.SearchResult.SearchLog.Abn,
                    r.RenewalYears, LodgedAt = r.CompletedAt ?? r.LastAttemptAt ?? r.InitiatedAt,
                    r.AirwallexTransferStatus, r.BpayPaymentError,
                })
                .ToListAsync();
            var fees = asic.Value;
            var bpay = bills.Select(b => new TodayItem(
                "bpay", b.Id.ToString(), b.AirwallexTransferStatus ?? "TO_PAY", b.ClientId, b.LeadName ?? b.Email,
                b.BusinessName, b.Abn, fees.FeeFor(b.RenewalYears), b.RenewalYears, b.LodgedAt, null,
                b.BpayPaymentError, null, CanRetry: false)).Where(i => Mine(i.ClientId)).ToList();

            // ── ASIC keys ─────────────────────────────────────────────────────────
            var requests = await db.AsicKeyRequests.AsNoTracking()
                .Where(r => !r.IsTest && WorkQueues.KeyRequestOpen.Contains(r.Status))
                .OrderBy(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id, r.ClientId, Name = (r.GivenNames + " " + r.FamilyName).Trim(), r.BusinessName, r.Abn,
                    r.CreatedAt, r.ErrorMessage, Status = r.Status.ToString(),
                })
                .ToListAsync();
            requests = requests.Where(r => Mine(r.ClientId)).ToList();
            var keys = new List<TodayItem>();
            // Requests waiting to be sent read as one line: they are worked through the key tool together.
            var toSend = requests.Where(r => r.Status != nameof(AsicKeyRequestStatus.Failed)).ToList();
            if (forClient)
                keys.AddRange(toSend.Select(r => new TodayItem(
                    "keyRequest", r.Id.ToString(), r.Status, r.ClientId, r.Name, r.BusinessName, r.Abn, null, null,
                    r.CreatedAt, null, r.ErrorMessage, null, CanRetry: false)));
            else if (toSend.Count > 0)
                keys.Add(new TodayItem("keyRequests", "to-send", "ToSend", null, null, null, null, null, null,
                    toSend.Min(r => r.CreatedAt), null, null, null, CanRetry: false, Count: toSend.Count));
            keys.AddRange(requests.Where(r => r.Status == nameof(AsicKeyRequestStatus.Failed)).Select(r => new TodayItem(
                "keyRequest", r.Id.ToString(), r.Status, r.ClientId, r.Name, r.BusinessName, r.Abn, null, null,
                r.CreatedAt, null, r.ErrorMessage, null, CanRetry: false)));
            var letters = await db.AsicKeyNotifications.AsNoTracking()
                .Where(n => !WorkQueues.KeyLetterSettled.Contains(n.Status))
                .OrderBy(n => n.ReceivedAt)
                .Select(n => new { n.Id, n.BusinessName, n.Abn, n.ReceivedAt, n.ErrorMessage, Status = n.Status.ToString() })
                .ToListAsync();
            if (forClient)
                letters = letters.Where(n => clientAbns.Contains(Helpers.NormalizeAbn(n.Abn))).ToList();
            keys.AddRange(letters.Select(n => new TodayItem(
                "keyLetter", n.Id.ToString(), n.Status, null, null, n.BusinessName, n.Abn, null, null,
                n.ReceivedAt, null, n.ErrorMessage, null,
                CanRetry: n.Status != nameof(AsicKeyNotificationStatus.ContactNotFound))));

            // ── Decide ────────────────────────────────────────────────────────────
            var duplicates = await db.Clients.AsNoTracking()
                .Where(c => !c.IsTest && c.PossibleDuplicateOfId != null)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id, c.FullName, c.CreatedAt,
                    OtherId = c.PossibleDuplicateOfId, OtherName = c.PossibleDuplicateOf != null ? c.PossibleDuplicateOf.FullName : null,
                })
                .ToListAsync();
            if (forClient)
                duplicates = duplicates.Where(c => c.Id == clientId || c.OtherId == clientId).ToList();
            var decide = duplicates.Select(c => new TodayItem(
                "duplicate", c.Id.ToString(), "PossibleDuplicate", c.Id, c.FullName, null, null, null, null,
                c.CreatedAt, null, null, null, CanRetry: false,
                OtherClientId: c.OtherId, OtherClientName: c.OtherName)).ToList();

            // Names come from the client record where the item is linked to one.
            var all = tryAgain.Take(rowsPerGroup).Concat(checkFirst.Take(rowsPerGroup)).Concat(decideMoney.Take(rowsPerGroup))
                .Concat(bpay.Take(rowsPerGroup)).Concat(keys.Take(rowsPerGroup)).ToList();
            var clientIds = all.Where(i => i.ClientId != null).Select(i => i.ClientId!.Value).Distinct().ToList();
            var clientNames = await db.Clients.AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName);
            List<TodayItem> Named(IEnumerable<TodayItem> items) => items.Take(rowsPerGroup)
                .Select(i => i.ClientId is { } id && clientNames.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)
                    ? i with { ClientName = n }
                    : i)
                .ToList();

            var groups = new[]
            {
                new TodayGroup("retry", tryAgain.Count, tryAgain.Sum(i => i.Amount ?? 0), Named(tryAgain)),
                new TodayGroup("check", checkFirst.Count, checkFirst.Sum(i => i.Amount ?? 0), Named(checkFirst)),
                new TodayGroup("refund", decideMoney.Count, decideMoney.Sum(i => i.Amount ?? 0), Named(decideMoney)),
                new TodayGroup("bpay", bpay.Count, bpay.Sum(i => i.Amount ?? 0), Named(bpay)),
                // Key requests count one per request, though the ones to send share a line.
                new TodayGroup("keys", requests.Count + letters.Count, null, Named(keys)),
                new TodayGroup("decide", decide.Count, null, Named(decide)),
            };

            // A broken OTP conversation holds every card renewal: it goes above everything.
            string? otpProblem = null;
            try { if (!forClient) otpProblem = await sms.CheckAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Today: OTP check failed"); }

            // ASIC refusing our card: renewals still go through by BPAY, but the card needs fixing.
            string? cardProblem = null;
            try { if (!forClient) cardProblem = (await AsicCardHealth.CheckAsync(db, now)).Problem; }
            catch (Exception ex) { logger.LogWarning(ex, "Today: card check failed"); }

            // Email not going out (SendGrid refusing our key, or every recent send failing).
            string? emailProblem = null;
            try { if (!forClient) emailProblem = await EmailEvents.ProblemAsync(db, now); }
            catch (Exception ex) { logger.LogWarning(ex, "Today: email check failed"); }

            return Results.Ok(new
            {
                generatedAt = now,
                total = groups.Sum(g => g.Count),
                otpProblem,
                cardProblem,
                emailProblem,
                groups,
            });
        }).RequireAuthorization().WithTags("Admin.Today");
    }


}
