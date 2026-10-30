using Carter;
using Hangfire;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;
using Stripe;

namespace Renewtron.Modules;

/// <summary>
/// Chargebacks. Stripe calls the webhook when a customer disputes a charge; we queue a job
/// that stages our evidence on the dispute for Smart Disputes to submit (see
/// <see cref="IDisputeEvidenceService"/>). The admin route re-runs it by hand, e.g. for a
/// dispute opened before the webhook existed.
/// </summary>
public sealed class DisputesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Stripe dashboard → Developers → Webhooks: endpoint {site}/api/stripe/webhook,
        // event charge.dispute.created; its signing secret goes in Settings → Stripe.
        app.MapPost("/api/stripe/webhook", async (
            HttpRequest request,
            IOptionsSnapshot<StripeSettings> stripe,
            IBackgroundJobClient jobs,
            ILogger<DisputesModule> logger) =>
        {
            var secret = stripe.Value.WebhookSecret;
            if (string.IsNullOrWhiteSpace(secret))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            // Stripe.net throws a NullReferenceException rather than a StripeException without it.
            var signature = request.Headers["Stripe-Signature"].ToString();
            if (string.IsNullOrEmpty(signature))
                return Results.BadRequest();

            var json = await new StreamReader(request.Body).ReadToEndAsync();
            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, signature, secret, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                logger.LogWarning("Rejected Stripe webhook: {Error}", ex.Message);
                return Results.BadRequest();
            }

            if (stripeEvent.Type == EventTypes.ChargeDisputeCreated && stripeEvent.Data.Object is Dispute dispute)
            {
                logger.LogInformation("Dispute {DisputeId} opened: {Reason}, {Amount} {Currency}",
                    dispute.Id, dispute.Reason, dispute.Amount, dispute.Currency);
                var testMode = !stripeEvent.Livemode;
                jobs.Enqueue<IDisputeEvidenceService>(s => s.PrepareEvidenceAsync(dispute.Id, false, testMode));
            }
            return Results.Ok();
        }).WithTags("Stripe");

        app.MapPost("/api/admin/disputes/{disputeId}/evidence", async (
            string disputeId,
            IDisputeEvidenceService evidence,
            bool force = false,
            bool test = false) =>
        {
            try
            {
                return Results.Ok(await evidence.PrepareEvidenceAsync(disputeId, force, test));
            }
            catch (StripeException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization().WithTags("Admin.Disputes");
    }
}
