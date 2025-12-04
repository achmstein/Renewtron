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
    /// Lead successfully completed renewal and payment
    /// </summary>
    RenewalCompleted = 5
}
