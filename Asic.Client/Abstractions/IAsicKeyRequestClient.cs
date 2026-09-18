namespace Asic.Client.Abstractions;

/// <summary>What goes into ASIC's online enquiry form when asking for a business name's ASIC key.</summary>
public sealed class AsicKeyRequestInput
{
    public string GivenNames { get; init; } = "";
    public string FamilyName { get; init; } = "";
    /// <summary>Entity number on the form — the ABN, digits only.</summary>
    public string Abn { get; init; } = "";
    /// <summary>Entity name on the form.</summary>
    public string BusinessName { get; init; } = "";
    /// <summary>Where ASIC should send the key — the inbox Renewtron scans.</summary>
    public string Email { get; init; } = "";
    public string PhonePrefix { get; init; } = "";
    public string PhoneNumber { get; init; } = "";
    /// <summary>The free-text enquiry ("My name is … please email a copy of my ASIC key to …").</summary>
    public string Question { get; init; } = "";
}

public sealed class AsicKeyRequestResult
{
    public bool Success { get; init; }
    /// <summary>ASIC's "Reference Number" from the Thank You page.</summary>
    public string ReferenceNumber { get; init; }
    public string ErrorMessage { get; init; }
    /// <summary>How many captcha tokens were bought for this submission.</summary>
    public int CaptchaAttempts { get; init; }
    /// <summary>True when a later attempt could plausibly succeed (low captcha score, network blip).</summary>
    public bool Transient { get; init; }

    public static AsicKeyRequestResult Succeeded(string reference, int captchaAttempts) =>
        new() { Success = true, ReferenceNumber = reference, CaptchaAttempts = captchaAttempts };

    public static AsicKeyRequestResult Failed(string error, int captchaAttempts, bool transient) =>
        new() { Success = false, ErrorMessage = error, CaptchaAttempts = captchaAttempts, Transient = transient };
}

/// <summary>
/// Drives ASIC's public "Online Enquiry" form (edge.asic.gov.au) to ask for a business
/// name's ASIC key: Business Name → Maintain information, then the details page.
/// </summary>
public interface IAsicKeyRequestClient
{
    Task<AsicKeyRequestResult> SubmitAsync(AsicKeyRequestInput input, double minCaptchaScore, int maxCaptchaAttempts, CancellationToken ct = default);
}
