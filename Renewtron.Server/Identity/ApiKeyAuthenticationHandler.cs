using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Renewtron.Identity;

/// <summary>
/// Header-based API key authentication for machine-to-machine callers (e.g. mastertron).
/// The expected key comes from configuration ("Security:ApiKey", i.e. the Security__ApiKey
/// environment variable at deploy). When the key is not configured, or the request carries
/// no <c>X-Api-Key</c> header, the handler returns NoResult so cookie auth proceeds as usual.
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

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Read per-request so env vars and the runtime settings.overrides.json layer both apply.
        var expectedKey = configuration["Security:ApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        var providedKey = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(providedKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedKey),
                Encoding.UTF8.GetBytes(expectedKey)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "mastertron"),
                new Claim(ClaimTypes.NameIdentifier, "mastertron"),
            ],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // Leave challenges to the cookie scheme so the SPA's unauthenticated behaviour
    // is unchanged; API clients simply see the authorization failure.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        => Task.CompletedTask;
}
