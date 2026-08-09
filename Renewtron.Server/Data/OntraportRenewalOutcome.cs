namespace Renewtron.Data;

/// <summary>
/// What we write back to the Ontraport contact's "Renewal status" field (f5481) once a
/// renewal attempt finishes. This is what answers "did we renew it, or had someone
/// already renewed it?" when a customer asks for a refund.
/// </summary>
public enum OntraportRenewalOutcome
{
    /// <summary>We renewed it with ASIC. The due date (f5135) is rolled forward too.</summary>
    Successful,
    /// <summary>ASIC already had a renewal in flight for this business name — we didn't charge it again.</summary>
    AlreadyProcessed,
    /// <summary>ASIC won't accept a renewal yet; its renewal window hasn't opened.</summary>
    NotYetDue,
    /// <summary>The renewal attempt failed and needs a look.</summary>
    Failed,
}

public static class OntraportRenewalOutcomeExtensions
{
    /// <summary>
    /// The literal text written to f5481. If that field is a dropdown in Ontraport its
    /// options have to match these strings exactly, otherwise the value won't stick.
    /// </summary>
    public static string ToFieldValue(this OntraportRenewalOutcome outcome) => outcome switch
    {
        OntraportRenewalOutcome.Successful => "Successful",
        OntraportRenewalOutcome.AlreadyProcessed => "Already Processed",
        OntraportRenewalOutcome.NotYetDue => "Not Yet Due",
        _ => "Failed",
    };

    public static OntraportRenewalOutcome FromSaleStatus(OntraportSaleStatus status) => status switch
    {
        OntraportSaleStatus.RenewalCompleted => OntraportRenewalOutcome.Successful,
        OntraportSaleStatus.RenewalInProgress => OntraportRenewalOutcome.AlreadyProcessed,
        OntraportSaleStatus.AsicNotYetDue => OntraportRenewalOutcome.NotYetDue,
        OntraportSaleStatus.NotDueForRenewal => OntraportRenewalOutcome.NotYetDue,
        _ => OntraportRenewalOutcome.Failed,
    };
}
