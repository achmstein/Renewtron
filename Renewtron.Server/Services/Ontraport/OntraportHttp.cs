using Microsoft.Extensions.Options;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// The one place Renewtron's HTTP calls to Ontraport get their address and keys. Keys are read
/// on every request, so a key changed in Settings applies to the next call without a restart.
/// </summary>
public sealed class OntraportAuthHandler(IOptionsMonitor<OntraportSettings> settings) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = settings.CurrentValue;
        request.Headers.Remove("Api-Appid");
        request.Headers.Remove("Api-Key");
        request.Headers.TryAddWithoutValidation("Api-Appid", current.ApiAppId);
        request.Headers.TryAddWithoutValidation("Api-Key", current.ApiKey);
        return base.SendAsync(request, cancellationToken);
    }
}

public static class OntraportHttpExtensions
{
    public static readonly Uri BaseAddress = new("https://api.ontraport.com/1/");

    /// <summary>Points a typed HttpClient at Ontraport's REST API with our app id and key.</summary>
    public static IHttpClientBuilder AddOntraportApi(this IHttpClientBuilder builder)
    {
        builder.Services.AddTransient<OntraportAuthHandler>();
        return builder
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = BaseAddress;
                c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<OntraportAuthHandler>();
    }
}
