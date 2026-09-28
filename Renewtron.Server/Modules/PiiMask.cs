namespace Renewtron.Modules;

/// <summary>
/// Display-only masking for contact details returned by public (unauthenticated) wizard
/// endpoints: enough for the customer to recognise their own details, not enough to use.
/// </summary>
public static class PiiMask
{
    private const string Dot = "\u2022";

    /// <summary>"David Orth" → "David O."</summary>
    public static string Name(string? fullName)
    {
        var parts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }

    /// <summary>"david@nighttax.com.au" → "d•••@nighttax.com.au"</summary>
    public static string Email(string? email)
    {
        var value = (email ?? "").Trim();
        var at = value.IndexOf('@');
        if (at <= 0) return value.Length == 0 ? "" : Dot + Dot + Dot;
        return $"{value[0]}{Dot}{Dot}{Dot}{value[at..]}";
    }

    /// <summary>"0422372425" → "•••• ••• 425"</summary>
    public static string Mobile(string? mobile)
    {
        var digits = new string((mobile ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 3) return digits.Length == 0 ? "" : Dot + Dot + Dot;
        return $"{Dot}{Dot}{Dot}{Dot} {Dot}{Dot}{Dot} {digits[^3..]}";
    }
}
