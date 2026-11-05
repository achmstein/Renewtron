using Asic.Client.Abstractions;
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
        int RenewalsToFix, int RenewalsStuck, int SalesNotRenewed, int BpayToPay, int KeyRequests, int KeysToSort, int Duplicates)
    {
        public int PaidNotRenewed => RenewalsToFix + RenewalsStuck + SalesNotRenewed;
        public int Total => PaidNotRenewed + BpayToPay + KeyRequests + KeysToSort + Duplicates;
    }

    /// <summary>
    /// Ontraport sales the client paid for that never got a renewal and won't get one by
    /// themselves. Sales that did start a renewal are left out: the renewal is the row to act on.
    /// </summary>
    private static IQueryable<OntraportSale> SalesNotRenewed(ApplicationDbContext db) =>
        db.OntraportSales.AsNoTracking()
            .Where(s => s.RenewalRequestId == null
                && (s.Status == OntraportSaleStatus.RenewalFailed || s.Status == OntraportSaleStatus.IneligibleForRenewal));

    private static IQueryable<RenewalRequest> Renewals(ApplicationDbContext db) =>
        db.RenewalRequests.AsNoTracking().Where(r => !r.IsTest);

    public static async Task<Counts> CountAsync(ApplicationDbContext db, DateTime now) => new(
        RenewalsToFix: await Renewals(db).Where(WorkQueues.RenewalNeedsPerson).CountAsync(),
        RenewalsStuck: await Renewals(db).Where(WorkQueues.RenewalStuck(now)).CountAsync(),
        SalesNotRenewed: await SalesNotRenewed(db).CountAsync(),
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
            ILogger<TodayModule> logger) =>
        {
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);

            // ── Paid, not renewed yet ─────────────────────────────────────────────
            var failed = await Renewals(db).Where(WorkQueues.RenewalNeedsPerson).Select(RenewalRow(db)).ToListAsync();
            var stuck = await Renewals(db).Where(WorkQueues.RenewalStuck(now)).Select(RenewalRow(db)).ToListAsync();
            var sales = await SalesNotRenewed(db)
                .Select(s => new
                {
                    s.Id, s.ClientId, s.ContactName, s.BusinessName, s.Abn, s.AmountPaid, s.RenewalYears,
                    s.SyncedAt, s.ProcessedAt, s.RenewalDueDate, s.ErrorMessage, Status = s.Status.ToString(),
                    NameDue = db.ClientBusinessNames.Where(n => n.Id == s.ClientBusinessNameId).Select(n => n.RenewalDueDate).FirstOrDefault(),
                })
                .ToListAsync();

            var paid = new List<TodayItem>();
            paid.AddRange(failed.Select(r => r.ToItem(r.ErrorCategory ?? "Failed",
                canRetry: r.FailedAtStep != "Complete Payment Action" && !r.HasHostedToken
                    && r.ErrorCategory != RenewalErrorCategories.PaymentRisk)));
            paid.AddRange(stuck.Select(r => r.ToItem("Stuck", canRetry: false)));
            paid.AddRange(sales.Select(s => new TodayItem(
                "sale", s.Id.ToString(), s.Status, s.ClientId, s.ContactName, s.BusinessName, s.Abn, s.AmountPaid,
                s.RenewalYears, s.ProcessedAt ?? s.SyncedAt,
                s.NameDue ?? (s.RenewalDueDate is { } d ? DateOnly.FromDateTime(d) : null),
                s.ErrorMessage, null, CanRetry: false)));

            // Riskiest first: names due soonest (or just past due), then those with no due date,
            // then names long past due; oldest first within each.
            var paidOrdered = paid
                .OrderBy(i => i.DueAt is null ? 1 : i.DueAt >= today.AddDays(-30) ? 0 : 2)
                .ThenBy(i => i.DueAt)
                .ThenBy(i => i.At)
                .ToList();

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
                b.BpayPaymentError, null, CanRetry: false)).ToList();

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
            var keys = new List<TodayItem>();
            // Requests waiting to be sent read as one line: they are worked through the key tool together.
            var toSend = requests.Where(r => r.Status != nameof(AsicKeyRequestStatus.Failed)).ToList();
            if (toSend.Count > 0)
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
            var decide = duplicates.Select(c => new TodayItem(
                "duplicate", c.Id.ToString(), "PossibleDuplicate", c.Id, c.FullName, null, null, null, null,
                c.CreatedAt, null, null, null, CanRetry: false,
                OtherClientId: c.OtherId, OtherClientName: c.OtherName)).ToList();

            // Names come from the client record where the item is linked to one.
            var all = paidOrdered.Take(RowsPerGroup).Concat(bpay.Take(RowsPerGroup)).Concat(keys.Take(RowsPerGroup)).ToList();
            var clientIds = all.Where(i => i.ClientId != null).Select(i => i.ClientId!.Value).Distinct().ToList();
            var clientNames = await db.Clients.AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName);
            // SQL Server hands times back without a kind; they are UTC, so say so, or the browser
            // reads them as local time and "2 hours ago" is off by the UTC offset.
            List<TodayItem> Named(IEnumerable<TodayItem> items) => items.Take(RowsPerGroup)
                .Select(i => i with { At = DateTime.SpecifyKind(i.At, DateTimeKind.Utc) })
                .Select(i => i.ClientId is { } id && clientNames.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)
                    ? i with { ClientName = n }
                    : i)
                .ToList();

            var groups = new[]
            {
                new TodayGroup("paid", paidOrdered.Count, paidOrdered.Sum(i => i.Amount ?? 0), Named(paidOrdered)),
                new TodayGroup("bpay", bpay.Count, bpay.Sum(i => i.Amount ?? 0), Named(bpay)),
                // Key requests count one per request, though the ones to send share a line.
                new TodayGroup("keys", requests.Count + letters.Count, null, Named(keys)),
                new TodayGroup("decide", decide.Count, null, Named(decide)),
            };

            // A broken OTP conversation holds every card renewal: it goes above everything.
            string? otpProblem = null;
            try { otpProblem = await sms.CheckAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Today: OTP check failed"); }

            return Results.Ok(new
            {
                generatedAt = now,
                total = groups.Sum(g => g.Count),
                otpProblem,
                groups,
            });
        }).RequireAuthorization().WithTags("Admin.Today");
    }

    private sealed record RenewalRowData(
        Guid Id, Guid? ClientId, string? LeadName, string? Email, string? BusinessName, string? Abn, decimal Amount,
        int RenewalYears, DateTime At, DateOnly? NameDue, DateTime? SaleDue, string? ErrorMessage, string? FailedAtStep,
        string? ErrorCategory, bool HasHostedToken)
    {
        public TodayItem ToItem(string reason, bool canRetry) => new(
            "renewal", Id.ToString(), reason, ClientId, LeadName ?? Email, BusinessName, Abn, Amount, RenewalYears, At,
            NameDue ?? (SaleDue is { } d ? DateOnly.FromDateTime(d) : null),
            ErrorMessage, FailedAtStep, canRetry);
    }

    private static System.Linq.Expressions.Expression<Func<RenewalRequest, RenewalRowData>> RenewalRow(ApplicationDbContext db) =>
        r => new RenewalRowData(
            r.Id, r.ClientId, r.Lead != null ? r.Lead.FullName : null, r.Email,
            r.SearchResult.BusinessName, r.SearchResult.SearchLog.Abn, r.Amount, r.RenewalYears,
            r.LastAttemptAt ?? r.InitiatedAt,
            db.ClientBusinessNames.Where(n => n.Id == r.ClientBusinessNameId).Select(n => n.RenewalDueDate).FirstOrDefault(),
            db.OntraportSales.Where(s => s.RenewalRequestId == r.Id).Select(s => s.RenewalDueDate).FirstOrDefault(),
            r.ErrorMessage, r.FailedAtStep, r.ErrorCategory, r.HostedTokenizationId != null);
}
