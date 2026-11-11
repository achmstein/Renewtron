namespace Renewtron.Abstractions;

/// <summary>What happened when evidence was prepared for one dispute.</summary>
public record DisputeEvidenceResult(string DisputeId, bool Staged, string Message, int RenewalsMatched);

public interface IDisputeEvidenceService
{
    /// <summary>
    /// Finds the renewals behind a Stripe dispute and stages our evidence on it (customer,
    /// activity log, ASIC references, a service record and the confirmation email as PDFs),
    /// with Smart Disputes preferred so Stripe merges it into its own packet. Nothing is
    /// submitted to the bank: Stripe does that before the deadline unless someone acts first.
    /// </summary>
    /// <param name="force">Re-stage even if Renewtron already did.</param>
    /// <param name="testMode">Use the test customers' Stripe keys (a test-mode dispute).</param>
    Task<DisputeEvidenceResult> PrepareEvidenceAsync(string disputeId, bool force = false, bool testMode = false);
}
