using System.Text.RegularExpressions;

namespace Asic.KeyTool;

/// <summary>
/// One business name to ask ASIC about, plus what came back. This is the unit the console
/// works in: typed in by hand, or read from a CSV row, then submitted and written back out
/// with ASIC's reference number.
/// </summary>
public sealed class Enquiry
{
    /// <summary>Ontraport contact this came from; empty when it was typed in by hand.</summary>
    public string ContactId { get; set; } = "";
    public string GivenNames { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string Abn { get; set; } = "";
    public string BusinessName { get; set; } = "";
    /// <summary>Contact phone as it arrived; normalised at submit time.</summary>
    public string Phone { get; set; } = "";

    /// <summary>
    /// The renewal this request belongs to. A business name comes up for renewal every
    /// year against the same contact, so the due date is what separates this year's
    /// request from last year's in the history.
    /// </summary>
    public DateTime? RenewalDueDate { get; set; }

    // ---- outcome ---------------------------------------------------------------------
    public bool Submitted { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Error { get; set; }
    public int CaptchaSolves { get; set; }

    public string ContactName => $"{GivenNames} {FamilyName}".Trim();

    /// <summary>Where a full name is all we have (CSV "contactName", Ontraport's shape).</summary>
    public void SetContactName(string? fullName)
    {
        var (given, family) = SplitName(fullName);
        GivenNames = given;
        FamilyName = family;
    }

    /// <summary>What ASIC's form needs: fields trimmed, phone split, enquiry text rendered.</summary>
    public AsicKeyRequestInput ToInput(KeyToolSettings settings)
    {
        var email = (settings.RequestEmail ?? "").Trim();
        var (prefix, number) = ResolvePhone(Phone, settings);
        return new AsicKeyRequestInput
        {
            GivenNames = GivenNames.Trim(),
            FamilyName = FamilyName.Trim(),
            Abn = DigitsOnly(Abn),
            BusinessName = BusinessName.Trim(),
            Email = email,
            PhonePrefix = prefix,
            PhoneNumber = number,
            Question = Render(string.IsNullOrWhiteSpace(settings.MessageTemplate)
                ? KeyToolSettings.DefaultMessageTemplate
                : settings.MessageTemplate, email),
        };
    }

    /// <summary>Why this row can't be sent, or null when it's good to go.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(BusinessName)) return "business name is blank";
        if (string.IsNullOrWhiteSpace(GivenNames) || string.IsNullOrWhiteSpace(FamilyName)) return "contact name is blank";
        var abn = DigitsOnly(Abn);
        if (abn.Length == 0) return "ABN is blank";
        if (abn.Length != 11) return $"ABN has {abn.Length} digits, expected 11";
        return null;
    }

    // ---- helpers (ported from Renewtron's AsicKeyRequestService) ----------------------

    public static (string Given, string Family) SplitName(string? fullName)
    {
        var parts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => ("", ""),
            1 => (parts[0], parts[0]),
            _ => (string.Join(' ', parts[..^1]), parts[^1]),
        };
    }

    /// <summary>
    /// ASIC wants an area/prefix box (max 4) and a number box (max 15). Australian numbers
    /// arrive as "0412 345 678", "+61412345678" or "61412345678"; anything that doesn't
    /// normalise to ten digits falls back to the configured office number.
    /// </summary>
    public static (string Prefix, string Number) ResolvePhone(string? raw, KeyToolSettings settings)
    {
        var digits = DigitsOnly(raw);
        if (digits.StartsWith("61") && digits.Length == 11) digits = "0" + digits[2..];
        if (digits.Length == 10 && digits[0] == '0')
            return (digits[..2], digits[2..]);
        if (digits.Length is 8 or 9)
            return ((settings.DefaultPhonePrefix ?? "").Trim(), digits);
        return ((settings.DefaultPhonePrefix ?? "").Trim(), DigitsOnly(settings.DefaultPhoneNumber));
    }

    public string Render(string template, string email)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = GivenNames.Trim(),
            ["LastName"] = FamilyName.Trim(),
            ["Abn"] = DigitsOnly(Abn),
            ["BusinessName"] = BusinessName.Trim(),
            ["Email"] = email,
        };
        var text = Regex.Replace(template, @"\{(\w+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        return Regex.Replace(text, @"[ \t]+", " ").Trim();
    }

    public static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());
}
