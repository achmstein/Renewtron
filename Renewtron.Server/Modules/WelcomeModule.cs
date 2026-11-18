using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// The welcome page after an Ontraport checkout: the order form redirects the payer to
/// /welcome?cid=[Contact ID], and this signs them straight into the Business Portal.
///
/// The contact id is easy to guess (they go up one by one), so the link is handed out only:
/// - for a contact with a paid Ontraport invoice from the last <see cref="Window"/>,
/// - once per contact, ever (<see cref="OntraportSale.PortalLinkIssuedAt"/>): the payer arrives
///   seconds after paying, so they get it, and anyone after them gets the plain sign-in page,
/// - as a checkout link (not-account-before = the payment), which the portal honours only for an
///   account created by this purchase, never an existing one.
/// Nothing about the contact (email, name) is ever sent back.
/// </summary>
public sealed class WelcomeModule : ICarterModule
{
    public sealed record WelcomeRequest(string? ContactId);

    /// <summary>How long after paying the link can be handed out.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>Allowance for Ontraport's and our clocks not agreeing exactly.</summary>
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(2);

    public const string RateLimitPolicy = "welcome";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/welcome", async (
            WelcomeRequest body,
            IOntraportSalesService ontraport,
            ApplicationDbContext db,
            IOptionsSnapshot<PortalSettings> portal,
            ILogger<WelcomeModule> logger) =>
        {
            var contactId = (body.ContactId ?? "").Trim();
            if (contactId.Length is 0 or > 12 || !contactId.All(char.IsAsciiDigit))
                return Results.BadRequest(new { error = "No order to look up." });

            var loginUrl = portal.Value.LoginUrl(null);
            object SignIn(string why)
            {
                logger.LogInformation("Welcome page for Ontraport contact {ContactId}: no link ({Why})", contactId, why);
                return new { status = "signIn", loginUrl };
            }

            var now = DateTime.UtcNow;
            var paidAt = await ontraport.LastPaidAtAsync(contactId);
            // Ontraport records the invoice a moment after the form: the page asks again shortly.
            if (paidAt is null) return Results.Ok(new { status = "waiting", loginUrl });
            if (now - paidAt.Value > Window + ClockSlack) return Results.Ok(SignIn("paid too long ago"));

            // Bring the sale in now (the daily sync would only see it tomorrow morning), so the
            // portal can find the purchase. Null until Ontraport marks the contact as paid.
            var sale = await ontraport.SyncContactAsync(contactId);
            if (sale is null) return Results.Ok(new { status = "waiting", loginUrl });

            var url = portal.Value.CheckoutSignInUrl(sale.Email, DateTimeOffset.UtcNow, paidAt.Value - ClockSlack);
            if (url is null) return Results.Ok(SignIn("portal sign-in links aren't set up"));

            // Claimed in one statement, so two requests at the same moment can't both get a link.
            var claimed = await db.OntraportSales
                .Where(s => s.OntraportContactId == contactId && s.PortalLinkIssuedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.PortalLinkIssuedAt, now));
            if (claimed == 0) return Results.Ok(SignIn("link already handed out"));

            logger.LogInformation("Welcome page signed Ontraport contact {ContactId} into the portal (paid {PaidAt:u})", contactId, paidAt);
            return Results.Ok(new { status = "ready", url });
        }).WithTags("Wizard").RequireRateLimiting(RateLimitPolicy);
    }
}
