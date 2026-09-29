using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Renewtron.Identity;

/// <summary>
/// Header-based API key authentication for machine-to-machine callers. Two keys:
/// <list type="bullet">
/// <item><c>Security:ApiKey</c> — the full admin key (e.g. mastertron). Signs in as an
/// admin-equivalent principal that passes the default policy.</item>
/// <item><c>Security:PartnerApiKey</c> — the Business Portal's key. Signs in with a
/// <c>scope=partner</c> claim, which the default policy rejects, so it can only reach the
/// <c>/api/partner/*</c> endpoints (policy <see cref="PartnerPolicy"/>).</item>
/// </list>
/// Keys come from configuration (the Security__ApiKey / Security__PartnerApiKey env vars at
/// deploy). An unset key never matches; a request with no <c>X-Api-Key</c> header returns
/// NoResult so cookie auth proceeds as usual.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string ScopeClaim = "scope";
    public const string PartnerScope = "partner";
    public const string PartnerPolicy = "Partner";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Read per-request so env vars and the runtime settings.overrides.json layer both apply.
        var adminKey = configuration["Security:ApiKey"];
        var partnerKey = configuration["Security:PartnerApiKey"];
        if (string.IsNullOrEmpty(adminKey) && string.IsNullOrEmpty(partnerKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        var providedKey = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(providedKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        Claim[] claims;
        if (Matches(providedKey, adminKey))
        {
            claims =
            [
                new Claim(ClaimTypes.Name, "mastertron"),
                new Claim(ClaimTypes.NameIdentifier, "mastertron"),
            ];
        }
        else if (Matches(providedKey, partnerKey))
        {
            claims =
            [
                new Claim(ClaimTypes.Name, "business-portal"),
                new Claim(ClaimTypes.NameIdentifier, "business-portal"),
                new Claim(ScopeClaim, PartnerScope),
            ];
        }
        else
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool Matches(string provided, string? expected)
        => !string.IsNullOrEmpty(expected)
           && CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(provided),
               Encoding.UTF8.GetBytes(expected));

    public static bool IsPartner(ClaimsPrincipal user)
        => user.HasClaim(ScopeClaim, PartnerScope);

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // The API-key-only Partner policy challenges here; doing nothing left an
        // empty 200. Answer 401 unless another scheme (the cookie) already did.
        if (!Response.HasStarted && Response.StatusCode == StatusCodes.Status200OK)
            Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
