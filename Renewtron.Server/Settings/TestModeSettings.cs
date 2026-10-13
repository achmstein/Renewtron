namespace Renewtron.Settings;

/// <summary>
/// Test customers: anyone who enters one of <see cref="TestEmails"/> in the wizard goes through
/// the real site, emails and portal, but pays with Stripe's test keys, is never sent to ASIC
/// (the renewal is marked done with a TEST- reference) and is never pushed to Ontraport.
/// Bound from "TestMode"; edited on the admin Settings page.
/// </summary>
public class TestModeSettings
{
    /// <summary>Email addresses, separated by commas, semicolons or new lines. Case-insensitive.</summary>
    public string? TestEmails { get; set; }

    /// <summary>Stripe test-mode secret key (sk_test_…).</summary>
    public string? StripeSecretKey { get; set; }

    /// <summary>Stripe test-mode publishable key (pk_test_…).</summary>
    public string? StripePublishableKey { get; set; }

    public IReadOnlyList<string> Emails() =>
        (TestEmails ?? "")
            .Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .Distinct()
            .ToList();

    public bool IsTestEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && Emails().Contains(email.Trim().ToLowerInvariant());

    /// <summary>Both Stripe test keys are present and really are test keys.</summary>
    public bool StripeReady =>
        (StripeSecretKey ?? "").StartsWith("sk_test_", StringComparison.Ordinal)
        && (StripePublishableKey ?? "").StartsWith("pk_test_", StringComparison.Ordinal);
}
