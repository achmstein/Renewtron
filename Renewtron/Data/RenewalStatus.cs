namespace Renewtron.Data;

/// <summary>
/// Represents the status of a renewal request
/// </summary>
public enum RenewalStatus
{
    /// <summary>
    /// Renewal request has been created and queued for processing
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Renewal is currently being processed by ASIC
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Renewal completed successfully
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Renewal failed during processing
    /// </summary>
    Failed = 3
}
