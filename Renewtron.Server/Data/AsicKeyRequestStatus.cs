namespace Renewtron.Data;

public enum AsicKeyRequestStatus
{
    /// <summary>Queued; the next run will submit it to ASIC.</summary>
    Pending,
    /// <summary>A person sent the enquiry on ASIC's form and recorded the reference number; waiting for the key email.</summary>
    Submitted,
    /// <summary>A notification carrying this business name's key arrived in the inbox.</summary>
    KeyReceived,
    /// <summary>A person looked at it and couldn't send it; the note says why.</summary>
    Failed,
    /// <summary>Taken by a person who is filling in ASIC's form now.</summary>
    Manual,
}
