namespace Renewtron.Data;

public enum AsicKeyRequestStatus
{
    /// <summary>Queued; the next run will submit it to ASIC.</summary>
    Pending,
    /// <summary>ASIC accepted the enquiry and gave a reference number; waiting for the key email.</summary>
    Submitted,
    /// <summary>A notification carrying this business name's key arrived in the inbox.</summary>
    KeyReceived,
    /// <summary>Submission failed (captcha, validation, ASIC outage). Transient failures are auto-retried.</summary>
    Failed,
}
