using Asic.Client;
using Asic.Client.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAsic(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<IAsicRenewalClient, AsicRenewalClient>();
        services.AddTransient<IAsicPaymentClient, AsicPaymentClient>();

        return services;
    }
}
