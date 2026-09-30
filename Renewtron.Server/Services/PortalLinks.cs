using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Renewtron.Services;

/// <summary>
/// One-click sign-in links into the Business Portal. The portal verifies these with the same
/// shared key (Portal:MagicLinkSigningKey), redeems each jti once, and refuses admin accounts.
///
/// Token = P + "." + S, where P = base64url(UTF-8 {"email","exp","jti"[,"nab"]}) and
/// S = base64url(HMAC-SHA256(key, ASCII(P))). The format is a contract with the portal —
/// change both sides together.
///
/// "nab" (not-account-before, unix seconds) marks a link handed straight to the browser at
/// checkout rather than emailed: whoever pays typed the email, which proves nothing, so the
/// portal only honours it for an account created by that purchase, never an existing one.
/// </summary>
public static class PortalLinks
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    public static string CreateSignInToken(string email, string key, DateTimeOffset now, Guid jti, DateTimeOffset? notAccountBefore = null)
    {
        var payload = JsonSerializer.Serialize(new SignInPayload(
            email.Trim().ToLowerInvariant(),
            now.Add(Lifetime).ToUnixTimeSeconds(),
            jti.ToString("N"),
            notAccountBefore?.ToUnixTimeSeconds()));
        var p = Base64Url(Encoding.UTF8.GetBytes(payload));
        var s = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes(p)));
        return $"{p}.{s}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // Property names are lower-case on purpose: they're the wire format, in this order.
    private sealed record SignInPayload(
        string email, long exp, string jti,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? nab);
}
