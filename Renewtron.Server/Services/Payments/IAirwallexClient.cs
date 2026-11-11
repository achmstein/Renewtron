namespace Renewtron.Abstractions;

/// <summary>A transfer as Airwallex reports it. Status is Airwallex's (PROCESSING, SENT, PAID, FAILED…).</summary>
public record AirwallexTransfer(string Id, string Status, string? FailureReason);

/// <summary>
/// The outcome of a call. Exactly one of Transfer or Error is set. Uncertain means the call
/// may have reached Airwallex (timeout, 5xx), so a transfer might exist even though we got no answer.
/// </summary>
public record AirwallexResult(AirwallexTransfer? Transfer, string? Error, bool Uncertain = false);

public interface IAirwallexClient
{
    /// <summary>Pays one BPAY bill in AUD. request_id makes a resend of the same payment a no-op at Airwallex.</summary>
    Task<AirwallexResult> CreateBpayTransferAsync(string requestId, string billerCode, string customerReference,
        decimal amount, string reference, CancellationToken ct = default);

    Task<AirwallexResult> GetTransferAsync(string transferId, CancellationToken ct = default);

    /// <summary>The transfer created with this request_id, if any (Transfer null and no Error when none).</summary>
    Task<AirwallexResult> FindTransferByRequestIdAsync(string requestId, CancellationToken ct = default);

    /// <summary>Logs in with the saved keys; null when it worked, otherwise why not.</summary>
    Task<string?> TestConnectionAsync(CancellationToken ct = default);
}
