namespace Renewtron.Settings;

/// <summary>
/// How Renewtron uses ASIC's register search to read names' real renewal dates (the register
/// file no longer carries them: ASIC removed BN_RENEW_DT in April 2025). Shares the
/// "AsicRegistry" section with the connection settings (proxy, 2Captcha key: Asic.Client's
/// <c>AsicRegistryOptions</c>).
/// </summary>
public class AsicRegistrySettings
{
    /// <summary>Off = due dates fall back to Renewtron's estimates, as before.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>ABNs looked up per nightly due-date refresh: each is a real ASIC session and may cost a CAPTCHA.</summary>
    public int MaxLookupsPerRun { get; set; } = 100;

    /// <summary>A name whose date was read from ASIC is not read again for this many days.</summary>
    public int RecheckAfterDays { get; set; } = 30;

    /// <summary>Names due within this many days (or overdue) are rechecked once stale, to catch renewals made elsewhere.</summary>
    public int RecheckWhenDueWithinDays { get; set; } = 60;

    /// <summary>Stop a run after this many lookups fail in a row: ASIC is clearly not answering.</summary>
    public int ConsecutiveFailureLimit { get; set; } = 5;
}
