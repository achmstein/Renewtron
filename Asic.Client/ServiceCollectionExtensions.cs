using Asic.Client;
using Asic.Client.Abstractions;
using Asic.Client.Captcha;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAsic(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwoCaptchaSettings>()
            .BindConfiguration("TwoCaptcha")
            .ValidateDataAnnotations();

        services.AddScoped<ICaptchaSolver, TwoCaptchaSolver>();

        services.AddScoped<IAsicRegistrySearchClient, AsicRegistrySearchClient>();   
        services.AddScoped<IAsicRenewalClient, AsicRenewalClient>();
        services.AddScoped<IAsicPaymentClient, AsicPaymentClient>();

        return services;
    }
}
