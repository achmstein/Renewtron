namespace Renewtron.Settings;

/// <summary>
/// The Business Portal — where customers see their business names, renewals and ASIC keys
/// after buying here. Bound from "Portal" (Portal__BaseUrl at deploy). Empty BaseUrl means
/// no portal links anywhere.
/// </summary>
public class PortalSettings
{
    /// <summary>e.g. https://portal.example.com.au — no trailing slash needed.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The portal's sign-in page, pre-filled with the customer's email; null when unset.</summary>
    public string? LoginUrl(string? email)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) return null;
        var root = BaseUrl.Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(email)
            ? $"{root}/login"
            : $"{root}/login?email={Uri.EscapeDataString(email.Trim())}";
    }
}
