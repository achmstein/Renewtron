using Carter;
using Microsoft.Extensions.Options;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// Public, non-secret config the SPA needs before it can render: marketing tag ids and the
/// Stripe publishable key. Serving it at runtime keeps the ids admin-editable — the payment
/// page used to reach for /api/admin/settings and quietly fall back to a build-time env var.
/// </summary>
public sealed class SiteConfigModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/site-config", (
            IOptionsSnapshot<TrackingSettings> tracking,
            IOptionsSnapshot<StripeSettings> stripe,
            IOptionsSnapshot<PricingSettings> pricing) =>
        {
            var t = tracking.Value;
            return Results.Ok(new
            {
                stripePublishableKey = stripe.Value.PublishableKey ?? string.Empty,
                pricing = new { oneYearFee = pricing.Value.OneYearFee, threeYearFee = pricing.Value.ThreeYearFee },
                tracking = new
                {
                    gtmContainerId = t.GtmContainerId,
                    ga4MeasurementId = t.Ga4MeasurementId,
                    metaPixelId = t.MetaPixelId,
                },
            });
        }).WithTags("Wizard");
    }
}
