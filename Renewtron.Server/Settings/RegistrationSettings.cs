namespace Renewtron.Settings;

/// <summary>
/// Registering new business names from the public /register page. The ASIC Connect login is the
/// one in <see cref="AsicPortalSettings"/>; ASIC's fee is paid the way <see cref="AsicSettings"/>
/// says (card, or BPAY through Airwallex).
/// </summary>
public class RegistrationSettings
{
    /// <summary>Shows the public register page and takes payments for it.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Lodges with ASIC. Off: each paid application is filled in up to ASIC's Review page and then
    /// cancelled (a dry run), so the whole flow can be tried without lodging anything.
    /// </summary>
    public bool Lodge { get; set; } = false;

    /// <summary>What the customer pays us, ASIC's fee included.</summary>
    public decimal OneYearPrice { get; set; } = 0m;
    public decimal ThreeYearPrice { get; set; } = 0m;

    /// <summary>
    /// Let through names ASIC refers for a manual decision. ASIC lodges those without asking for
    /// payment and may refuse them later, so they're stopped (and refunded) unless this is on.
    /// </summary>
    public bool AllowManualDecision { get; set; } = false;

    public decimal? PriceFor(int years)
    {
        var price = years == 3 ? ThreeYearPrice : years == 1 ? OneYearPrice : 0m;
        return price > 0 ? price : null;
    }
}
