namespace Renewtron.Data;

/// <summary>
/// Represents the outcome of a lead after ASIC lookup
/// </summary>
public enum LeadOutcome
{
    /// <summary>
    /// Lead captured, ASIC check not yet performed
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Business names found and available for renewal
    /// </summary>
    RenewalAvailable = 1,

    /// <summary>
    /// Business name is already registered or not due for renewal yet
    /// </summary>
    NotDueForRenewal = 2,

    /// <summary>
    /// A renewal for this ABN is already in progress
    /// </summary>
    RenewalInProgress = 3,

    /// <summary>
    /// No business names found for this ABN
    /// </summary>
    NoBusinessNames = 4,

    /// <summary>
    /// Old: written over what ASIC said once they paid. Paying is now <see cref="LeadProgress.Paid"/>;
    /// no longer written (Nov 2026), and old rows were moved back to RenewalAvailable.
    /// </summary>
    RenewalCompleted = 5
}

/// <summary>How far a visitor got on the renewal site, step by step. Kept apart from what ASIC said.</summary>
public enum LeadProgress
{
    /// <summary>Gave their details; no names shown (ASIC had none to renew, or they left).</summary>
    Details = 0,
    /// <summary>Saw their business names to pick from.</summary>
    NamesShown = 1,
    /// <summary>Opened the payment page (card form) and didn't pay.</summary>
    PaymentPage = 2,
    Paid = 3,
}
