namespace Renewtron.Data;

public enum AsicKeyNotificationStatus
{
    /// <summary>Email discovered; PDF not yet fetched (or a retry was requested).</summary>
    Pending,
    /// <summary>ASIC key extracted and written to the Ontraport contact.</summary>
    Completed,
    /// <summary>The email body had no usable "Click here" download link.</summary>
    LinkNotFound,
    /// <summary>The link didn't yield a PDF — typically expired (ASIC keeps them 30 days).</summary>
    DownloadFailed,
    /// <summary>PDF fetched but the key pattern didn't match its text.</summary>
    KeyNotFound,
    /// <summary>Key extracted, but no Ontraport contact carries this business name / ABN.</summary>
    ContactNotFound,
    /// <summary>Key extracted and contact found, but the Ontraport write failed.</summary>
    OntraportUpdateFailed,
}
