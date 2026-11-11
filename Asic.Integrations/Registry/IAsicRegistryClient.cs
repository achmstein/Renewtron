
namespace Asic.Integrations.Registry;

/// <summary>ASIC's public business-names register search (connectonline.asic.gov.au), CAPTCHA solved as needed.</summary>
public interface IAsicRegistryClient
{
    /// <summary>Every business name held under an ABN, each with its detail page read.</summary>
    Task<AsicRegistrySearchResult> SearchByAbnAsync(string abn, CancellationToken cancellationToken);

    /// <summary>The single name matching <paramref name="businessName"/> when the ABN holds several.</summary>
    Task<AsicRegistrySearchResult> SearchByAbnAndNameAsync(string abn, string businessName, CancellationToken cancellationToken);
}

/// <summary>A fresh client per lookup: each owns an isolated cookie session. Dispose it when done.</summary>
public interface IAsicRegistryClientFactory
{
    IAsicRegistryClient Create();
}

/// <summary>Solves the reCAPTCHA ASIC puts in front of the register search.</summary>
public interface ICaptchaSolver
{
    bool IsConfigured { get; }

    Task<CaptchaSolveResult> SolveReCaptchaAsync(
        string siteKey, string pageUrl, string? action, bool invisible, bool enterprise, CancellationToken cancellationToken);
}
