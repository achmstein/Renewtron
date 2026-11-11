using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services.Email;

/// <summary>
/// The values each email's template gets, built from real records. Sending and "preview with real data"
/// both use these, so a preview shows exactly what that customer would get. Names match
/// <see cref="EmailTemplateCatalog"/>; values arrive raw and the renderer encodes them.
/// </summary>
public static class EmailModels
{
    public static Dictionary<string, object?> RenewalConfirmation(
        PortalSettings portal, string toEmail, string businessName, string abn, int renewalYears,
        decimal amountPaid, string transactionReference, DateTime renewedAtUtc)
    {
        // Customers keep an account in the Business Portal. With a signing key the button signs them
        // straight in (once, for 72 hours); otherwise it opens the sign-in page with their email filled in.
        var signInUrl = portal.SignInUrl(toEmail, DateTimeOffset.UtcNow);
        var portalUrl = signInUrl ?? portal.LoginUrl(toEmail);
        return new()
        {
            ["business_name"] = businessName,
            ["abn"] = abn,
            ["renewal_period"] = $"{renewalYears} {(renewalYears == 1 ? "Year" : "Years")}",
            ["amount_paid"] = Money(amountPaid),
            ["transaction_reference"] = transactionReference,
            ["date"] = $"{renewedAtUtc:MMMM dd, yyyy 'at' hh:mm tt} UTC",
            ["portal_url"] = portalUrl ?? "",
            ["login_url"] = portal.LoginUrl(null) ?? "",
            ["has_sign_in_link"] = signInUrl is not null,
            ["has_login_link"] = signInUrl is null && portalUrl is not null,
            ["email"] = toEmail,
        };
    }

    public static Dictionary<string, object?> Registration(Data.BusinessNameRegistration r, string? reason, DateTime atUtc) => new()
    {
        ["full_name"] = $"{r.FirstName} {r.LastName}".Trim(),
        ["business_name"] = r.BusinessName,
        ["abn"] = r.Abn.Length == 11 ? $"{r.Abn[..2]} {r.Abn[2..5]} {r.Abn[5..8]} {r.Abn[8..]}" : r.Abn,
        ["registration_period"] = $"{r.Years} {(r.Years == 1 ? "Year" : "Years")}",
        ["amount_paid"] = Money(r.Amount),
        ["transaction_reference"] = r.AsicTransactionReference ?? "",
        ["reason"] = reason ?? "",
        ["date"] = $"{atUtc:MMMM dd, yyyy}",
        ["email"] = r.Email,
    };

    public static Dictionary<string, object?> PaidEarly(string toEmail, string abn, string[] businessNames, int renewalYears, decimal amountPaid)
    {
        var one = businessNames.Length == 1;
        return new()
        {
            ["business_names"] = businessNames,
            ["first_business_name"] = businessNames.FirstOrDefault() ?? "",
            ["names_count"] = businessNames.Length,
            ["is_one_name"] = one,
            ["abn"] = abn,
            ["amount_paid"] = Money(amountPaid),
            ["term"] = $"{renewalYears} {(renewalYears == 1 ? "year" : "years")}",
            ["your_names"] = one ? "your business name" : "your business names",
            ["its"] = one ? "its" : "their",
            ["it_has"] = one ? "it has" : "they have",
            ["email"] = toEmail,
        };
    }

    /// <summary>Values for the emails about a lead (lead captured, not due, no names, in progress, win-back).</summary>
    public static Dictionary<string, object?> Lead(Lead lead, string? firstBusinessName = null) => new()
    {
        ["full_name"] = lead.FullName ?? "",
        ["abn"] = FormatAbn(lead.Abn),
        ["email"] = lead.Email ?? "",
        ["date"] = $"{lead.CreatedAt:MMMM dd, yyyy}",
        ["reminder_opt_in"] = lead.ReminderOptIn,
        ["business_name"] = firstBusinessName ?? "your business name",
    };

    public static string Money(decimal amount) => $"${amount:F2}";

    public static string FormatAbn(string? abn)
    {
        if (abn is null) return "";
        var clean = abn.Replace(" ", "");
        return clean.Length == 11 ? $"{clean[..2]} {clean[2..5]} {clean[5..8]} {clean[8..]}" : abn;
    }
}
