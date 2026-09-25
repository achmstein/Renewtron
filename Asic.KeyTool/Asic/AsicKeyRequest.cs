namespace Asic.KeyTool;

/// <summary>What goes into ASIC's online enquiry form when asking for a business name's ASIC key.</summary>
public sealed class AsicKeyRequestInput
{
    public string GivenNames { get; init; } = "";
    public string FamilyName { get; init; } = "";
    /// <summary>Entity number on the form — the ABN, digits only.</summary>
    public string Abn { get; init; } = "";
    /// <summary>Entity name on the form.</summary>
    public string BusinessName { get; init; } = "";
    /// <summary>The form's contact email — the client's own address.</summary>
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
    public string? ReferenceNumber { get; init; }
    public string? ErrorMessage { get; init; }
    /// <summary>Page loads spent getting a token ASIC would accept.</summary>
    public int Attempts { get; init; }
    /// <summary>True when a later attempt could plausibly succeed (low captcha score, network blip).</summary>
    public bool Transient { get; init; }
    /// <summary>True when the failure was ASIC scoring the browser's captcha below its minimum.</summary>
    public bool CaptchaRejected { get; init; }

    public static AsicKeyRequestResult Succeeded(string reference, int attempts) =>
        new() { Success = true, ReferenceNumber = reference, Attempts = attempts };

    public static AsicKeyRequestResult Failed(string error, int attempts, bool transient, bool captchaRejected = false) =>
        new() { Success = false, ErrorMessage = error, Attempts = attempts, Transient = transient, CaptchaRejected = captchaRejected };
}
