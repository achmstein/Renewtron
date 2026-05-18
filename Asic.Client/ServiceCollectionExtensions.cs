using Asic.Client;
using Asic.Client.Abstractions;
using Asic.Client.ThreeDS;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ASIC client services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <param name="configureSmsProvider">Optional action to configure the ISmsProvider implementation.
    /// If not provided, ISmsProvider must be registered separately before calling this method.</param>
    public static IServiceCollection AddAsic(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceCollection>? configureSmsProvider = null)
    {
        // Register SMS provider first (if provided)
        configureSmsProvider?.Invoke(services);

        // Register 3DS challenge handlers. Order doesn't matter — selection is by CanHandle(host).
        services.AddTransient<IThreeDSChallengeHandler, RsaChallengeHandler>();
        services.AddTransient<IThreeDSChallengeHandler, CardinalChallengeHandler>();
        services.AddTransient<IThreeDSChallengeHandler, ArcotChallengeHandler>();

        // Register ASIC clients (these depend on ISmsProvider + 3DS challenge handlers)
        services.AddTransient<IAsicRenewalClient, AsicRenewalClient>();
        services.AddTransient<IAsicPaymentClient, AsicPaymentClient>();

        return services;
    }
}
