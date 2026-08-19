namespace Renewtron.Modules;

internal static class Helpers
{
    public static string NormalizeAbn(string? input) =>
        new(string.IsNullOrEmpty(input) ? [] : input.Where(char.IsDigit).ToArray());

    public static bool IsValidAbn(string? input) => NormalizeAbn(input).Length == 11;

    public static (string? Ip, string? UserAgent) ClientInfo(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        var ua = ctx.Request.Headers.UserAgent.ToString();
        return (ip, string.IsNullOrWhiteSpace(ua) ? null : ua);
    }

    /// <summary>Parses a comma-separated enum list ("Failed,Pending"), ignoring unknown values.</summary>
    public static TEnum[] ParseEnumList<TEnum>(string? csv) where TEnum : struct, Enum =>
        (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<TEnum>(s, true, out var v) ? (TEnum?)v : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();
}
