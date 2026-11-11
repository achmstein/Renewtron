using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

public class BpayPaymentService : IBpayPaymentService
{
    /// <summary>Set by us: Airwallex refused to create the transfer, so nothing was sent.</summary>
    public const string Rejected = "REJECTED";

    /// <summary>Set by us: the create call got no clear answer, so a transfer may exist. The next
    /// try looks it up by request_id before sending again.</summary>
    public const string Unknown = "UNKNOWN";

    /// <summary>Statuses after which nothing was paid and the bill can be paid again.</summary>
    public static readonly HashSet<string> Ended =
        ["FAILED", "CANCELLED", "APPROVAL_REJECTED", "APPROVAL_RECALLED", "APPROVAL_BLOCKED", Rejected];

    public const string Paid = "PAID";

    /// <summary>A PAID transfer can still fail if the clearing system rejects it later.</summary>
    private static readonly TimeSpan WatchPaidFor = TimeSpan.FromDays(5);

    // One payment run at a time, so two clicks can't both see a bill as unpaid.
    private static readonly SemaphoreSlim PayLock = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IAirwallexClient _airwallex;
    private readonly AsicSettings _asic;
    private readonly AirwallexSettings _airwallexSettings;
    private readonly ILogger<BpayPaymentService> _logger;

    public BpayPaymentService(ApplicationDbContext db, IAirwallexClient airwallex, IOptionsSnapshot<AsicSettings> asic,
        IOptionsSnapshot<AirwallexSettings> airwallexSettings, ILogger<BpayPaymentService> logger)
    {
        _db = db;
        _airwallex = airwallex;
        _asic = asic.Value;
        _airwallexSettings = airwallexSettings.Value;
        _logger = logger;
    }

    /// <summary>Can be paid now: lodged to pay by BPAY, not yet paid, and no transfer under way.</summary>
    public static bool IsPayable(RenewalRequest r) =>
        !r.IsTest
        && !string.IsNullOrEmpty(r.BpayReference) && !string.IsNullOrEmpty(r.BpayBillerCode)
        && r.BpayPaidAt is null
        && (r.AirwallexTransferStatus is null || Ended.Contains(r.AirwallexTransferStatus) || r.AirwallexTransferStatus == Unknown);

    public async Task<(IReadOnlyList<BpayPayOutcome> Outcomes, string? Refused)> PayAsync(IReadOnlyCollection<Guid> renewalIds, decimal expectedTotal)
    {
        if (renewalIds.Count == 0) return ([], "Nothing selected.");
        if (!_airwallexSettings.Ready) return ([], "Airwallex isn't set up (Settings → Airwallex).");

        await PayLock.WaitAsync();
        try
        {
            var renewals = await _db.RenewalRequests
                .Include(r => r.SearchResult)
                .Where(r => renewalIds.Contains(r.Id))
                .ToListAsync();

            if (renewals.Count != renewalIds.Count)
                return ([], "Some of the selected renewals no longer exist. Refresh and try again.");
            if (renewals.FirstOrDefault(r => !IsPayable(r)) is { } done)
                return ([], $"{done.SearchResult?.BusinessName} is already paid or being paid. Refresh and try again.");
            if (renewals.FirstOrDefault(r => _asic.FeeFor(r.RenewalYears) is null) is { } noFee)
                return ([], $"ASIC's {noFee.RenewalYears}-year fee isn't set (Settings → ASIC credentials).");

            var total = renewals.Sum(r => _asic.FeeFor(r.RenewalYears)!.Value);
            if (total != expectedTotal)
                return ([], $"The total is now A${total:F2}, not A${expectedTotal:F2}. Refresh and check before paying.");

            var outcomes = new List<BpayPayOutcome>();
            foreach (var renewal in renewals)
                outcomes.Add(await PayOneAsync(renewal, _asic.FeeFor(renewal.RenewalYears)!.Value));
            return (outcomes, null);
        }
        finally
        {
            PayLock.Release();
        }
    }

    private async Task<BpayPayOutcome> PayOneAsync(RenewalRequest renewal, decimal amount)
    {
        // A new attempt gets a new request_id; after an unclear answer the same one is reused,
        // so if the earlier transfer did reach Airwallex it is found instead of sent twice.
        if (renewal.AirwallexTransferStatus != Unknown)
            renewal.BpayPaymentAttempts++;
        var requestId = RequestId(renewal);

        if (renewal.AirwallexTransferStatus == Unknown)
        {
            var found = await _airwallex.FindTransferByRequestIdAsync(requestId);
            if (found.Error is not null)
                return await Record(renewal, null, Unknown, $"Couldn't check the earlier payment: {found.Error}");
            if (found.Transfer is { } existing)
                return await Record(renewal, existing, existing.Status, existing.FailureReason);
        }

        // Saved before sending: if the process dies mid-call, the attempt is still on record.
        renewal.BpayAmount = amount;
        renewal.AirwallexTransferStatus = Unknown;
        renewal.BpayPaymentError = null;
        await _db.SaveChangesAsync();

        var result = await _airwallex.CreateBpayTransferAsync(requestId, renewal.BpayBillerCode!, renewal.BpayReference!,
            amount, Reference(renewal));

        if (result.Transfer is { } transfer)
            return await Record(renewal, transfer, transfer.Status, transfer.FailureReason);

        return await Record(renewal, null, result.Uncertain ? Unknown : Rejected, result.Error);
    }

    private async Task<BpayPayOutcome> Record(RenewalRequest renewal, AirwallexTransfer? transfer, string status, string? error)
    {
        if (transfer is not null && !string.IsNullOrEmpty(transfer.Id))
            renewal.AirwallexTransferId = transfer.Id;
        Apply(renewal, status, error);
        await _db.SaveChangesAsync();

        _logger.LogInformation("BPAY for renewal {RenewalId}: {Status} (transfer {TransferId}) {Error}",
            renewal.Id, status, renewal.AirwallexTransferId, error);
        return new BpayPayOutcome(renewal.Id, status is not (Rejected or Unknown), status, error);
    }

    private static void Apply(RenewalRequest renewal, string status, string? error)
    {
        renewal.AirwallexTransferStatus = status;
        renewal.BpayPaymentError = Ended.Contains(status) || status == Unknown ? Trim(error, 1000) : null;

        if (status == Paid)
            renewal.BpayPaidAt ??= DateTime.UtcNow;
        else if (Ended.Contains(status) && renewal.AirwallexTransferId is not null)
            renewal.BpayPaidAt = null; // includes a PAID transfer that failed later
    }

    public async Task<int> SyncAsync()
    {
        var watchFrom = DateTime.UtcNow - WatchPaidFor;
        var renewals = await _db.RenewalRequests
            .Where(r => r.AirwallexTransferStatus != null)
            .Where(r => !Ended.Contains(r.AirwallexTransferStatus!))
            .Where(r => r.AirwallexTransferStatus != Paid || r.BpayPaidAt >= watchFrom)
            .ToListAsync();

        var changed = 0;
        foreach (var renewal in renewals)
        {
            var result = renewal.AirwallexTransferId is { } id
                ? await _airwallex.GetTransferAsync(id)
                : await _airwallex.FindTransferByRequestIdAsync(RequestId(renewal));
            if (result.Error is not null)
            {
                _logger.LogWarning("BPAY sync for renewal {RenewalId}: {Error}", renewal.Id, result.Error);
                continue;
            }
            if (result.Transfer is not { } transfer) continue; // an unclear send that never arrived stays UNKNOWN

            if (transfer.Status == renewal.AirwallexTransferStatus && renewal.AirwallexTransferId == transfer.Id) continue;
            renewal.AirwallexTransferId = transfer.Id;
            Apply(renewal, transfer.Status, transfer.FailureReason);
            changed++;
        }

        if (changed > 0) await _db.SaveChangesAsync();
        return changed;
    }

    private static string RequestId(RenewalRequest r) => $"bpay-{r.Id:N}-{r.BpayPaymentAttempts}";

    /// <summary>Shown on Airwallex's side; BPAY itself carries only the customer reference.</summary>
    private static string Reference(RenewalRequest r) =>
        Trim($"ASIC {r.TransactionReference ?? r.SearchResult?.BusinessName}", 18)!;

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
