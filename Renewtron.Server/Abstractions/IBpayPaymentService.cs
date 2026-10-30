namespace Renewtron.Abstractions;

public record BpayPayOutcome(Guid RenewalId, bool Sent, string? Status, string? Error);

public interface IBpayPaymentService
{
    /// <summary>
    /// Pays the chosen renewals' BPAY bills through Airwallex, one transfer each, at ASIC's fee
    /// for the period. Refused as a whole when the total isn't what the person confirmed
    /// (a fee changed, or a bill was paid in the meantime).
    /// </summary>
    Task<(IReadOnlyList<BpayPayOutcome> Outcomes, string? Refused)> PayAsync(IReadOnlyCollection<Guid> renewalIds, decimal expectedTotal);

    /// <summary>Reads back the status of transfers still in flight (and recent PAID ones, which can still fail).</summary>
    Task<int> SyncAsync();
}
