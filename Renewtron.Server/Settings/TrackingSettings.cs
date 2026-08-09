namespace Renewtron.Settings;

/// <summary>
/// Public marketing tag ids. These are served to the browser by /api/site-config and are
/// not secrets — they're admin-editable so a tag can be swapped without a rebuild.
/// </summary>
public class TrackingSettings
{
    /// <summary>GTM container, e.g. GTM-XXXXXXX. Loaded instead of GA4 when both are set.</summary>
    public string GtmContainerId { get; set; } = string.Empty;

    /// <summary>GA4 measurement id, e.g. G-XXXXXXXXXX.</summary>
    public string Ga4MeasurementId { get; set; } = string.Empty;

    /// <summary>Meta (Facebook) pixel id.</summary>
    public string MetaPixelId { get; set; } = string.Empty;
}
