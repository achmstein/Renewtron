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

    /// <summary>Shared with the portal (≥32 chars). When set with BaseUrl, emails carry a
    /// one-click sign-in link instead of the plain sign-in page.</summary>
    public string? MagicLinkSigningKey { get; set; }

    /// <summary>A signed, single-use, 72-hour sign-in link for this customer; null when the
    /// portal or the signing key isn't configured (callers fall back to <see cref="LoginUrl"/>).</summary>
    public string? SignInUrl(string? email, DateTimeOffset now) => CreateSignInUrl(email, now, null);

    /// <summary>A sign-in link for the checkout's confirmation page. The payer typed the email,
    /// so the portal honours it only for an account created after this wizard run began
    /// (<paramref name="leadCreatedAt"/>), i.e. by this purchase — never an existing one.</summary>
    public string? CheckoutSignInUrl(string? email, DateTimeOffset now, DateTime leadCreatedAt) =>
        CreateSignInUrl(email, now, new DateTimeOffset(DateTime.SpecifyKind(leadCreatedAt, DateTimeKind.Utc)));

    private string? CreateSignInUrl(string? email, DateTimeOffset now, DateTimeOffset? notAccountBefore)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrEmpty(MagicLinkSigningKey) || MagicLinkSigningKey.Length < 32)
            return null;
        var token = Services.PortalLinks.CreateSignInToken(email, MagicLinkSigningKey, now, Guid.NewGuid(), notAccountBefore);
        return $"{BaseUrl.Trim().TrimEnd('/')}/auth/link?t={Uri.EscapeDataString(token)}";
    }

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
