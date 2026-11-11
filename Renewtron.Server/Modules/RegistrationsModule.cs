using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services.Registration;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// Registering a new business name. Public: the /register page's config, a quick name check
/// against our copy of the register, the application, its payment and its status. Admin: the
/// list, one application, retry, paying ASIC's BPAY bill and marking a refund.
/// </summary>
public sealed class RegistrationsModule : ICarterModule
{
    public record PayRequest(string PaymentMethodId);
    public record CompleteRequest(string PaymentIntentId);
    public record BpayRequest(decimal ExpectedAmount);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/api/registrations").WithTags("Register");

        pub.MapGet("/config", (IOptionsSnapshot<RegistrationSettings> settings, IOptionsSnapshot<StripeSettings> stripe) =>
        {
            var s = settings.Value;
            return Results.Ok(new
            {
                enabled = s.Enabled && s.PriceFor(1) is not null && s.PriceFor(3) is not null,
                oneYearPrice = s.OneYearPrice,
                threeYearPrice = s.ThreeYearPrice,
                stripePublishableKey = stripe.Value.PublishableKey ?? "",
            });
        });

        // Our copy of the register only knows names already registered; ASIC's own check runs
        // before anything is lodged (and catches similar names, banned words and names under application).
        pub.MapGet("/name-check", async (string? name, IBusinessNameRegistrationService service, CancellationToken ct) =>
        {
            var clean = BusinessNameRegistrationService.CleanName(name);
            if (clean.Length < 2) return Results.BadRequest(new { error = "Enter a business name." });
            var taken = await service.IsNameTakenLocallyAsync(clean, ct);
            return Results.Ok(new { name = clean, taken });
        }).RequireRateLimiting("lead-capture");

        pub.MapPost("/", async (RegistrationInput input, IBusinessNameRegistrationService service) =>
        {
            var (reg, problem) = await service.CreateAsync(input);
            return reg is null ? Results.BadRequest(new { error = problem }) : Results.Ok(new { id = reg.Id });
        }).RequireRateLimiting("lead-capture");

        pub.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db, IOptionsSnapshot<TestModeSettings> testMode) =>
        {
            var r = await db.BusinessNameRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            return Results.Ok(new
            {
                id = r.Id,
                businessName = r.BusinessName,
                abn = r.Abn,
                years = r.Years,
                amount = r.Amount,
                firstName = r.FirstName,
                email = PiiMask.Email(r.Email),
                paid = r.PaidAt is not null,
                stage = PublicStage(r),
                asicReference = r.LodgedAt is not null ? r.AsicTransactionReference : null,
                isTest = r.IsTest,
                // A test customer pays with Stripe's test keys.
                testStripePublishableKey = r.IsTest ? testMode.Value.StripePublishableKey : null,
            });
        });

        pub.MapPost("/{id:guid}/pay", async (Guid id, PayRequest body, IBusinessNameRegistrationService service) =>
            PayResult(await service.PayAsync(id, body.PaymentMethodId)));

        pub.MapPost("/{id:guid}/pay/complete", async (Guid id, CompleteRequest body, IBusinessNameRegistrationService service) =>
            PayResult(await service.CompletePaymentAsync(id, body.PaymentIntentId)));

        var admin = app.MapGroup("/api/admin/registrations").RequireAuthorization().WithTags("Admin.Registrations");

        admin.MapGet("/", async (ApplicationDbContext db, string? status = null, string? search = null, int page = 1, int pageSize = 25) =>
        {
            var statuses = Helpers.ParseEnumList<BusinessNameRegistrationStatus>(status);
            var q = db.BusinessNameRegistrations.AsNoTracking();
            if (statuses.Length > 0) q = q.Where(r => statuses.Contains(r.Status));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var t = search.Trim();
                q = q.Where(r => r.BusinessName.Contains(t) || r.Email.Contains(t) || r.Abn.Contains(t)
                                 || r.LastName.Contains(t) || (r.AsicTransactionReference != null && r.AsicTransactionReference.Contains(t)));
            }
            var total = await q.CountAsync();
            pageSize = Math.Clamp(pageSize, 1, 100);
            var rows = await q.OrderByDescending(r => r.CreatedAt).Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new { total, items = rows.Select(ToDto) });
        });

        admin.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db, IOptionsSnapshot<AsicSettings> asic) =>
        {
            var r = await db.BusinessNameRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            return Results.Ok(new { registration = ToDto(r), pageText = r.PageText, asicFeeNow = asic.Value.FeeFor(r.Years) });
        });

        admin.MapPost("/{id:guid}/retry", async (Guid id, IBusinessNameRegistrationService service) =>
        {
            var problem = await service.RetryAsync(id);
            return problem is null ? Results.Ok(new { message = "Queued for ASIC." }) : Results.Conflict(new { error = problem });
        });

        admin.MapPost("/{id:guid}/pay-asic", async (Guid id, BpayRequest body, IBusinessNameRegistrationService service) =>
        {
            var problem = await service.PayAsicByBpayAsync(id, body.ExpectedAmount);
            return problem is null ? Results.Ok(new { message = "BPAY payment sent through Airwallex." }) : Results.Conflict(new { error = problem });
        });

        admin.MapPost("/{id:guid}/refunded", async (Guid id, IBusinessNameRegistrationService service) =>
        {
            var problem = await service.MarkRefundedAsync(id);
            return problem is null ? Results.Ok(new { message = "Marked refunded." }) : Results.Conflict(new { error = problem });
        });
    }

    private static IResult PayResult(RegistrationPayResult r) =>
        r.Error is not null
            ? Results.UnprocessableEntity(new { error = r.Error })
            : Results.Ok(new { paid = r.Paid, requiresAction = r.RequiresAction, clientSecret = r.ClientSecret });

    /// <summary>What the customer sees: paid, with ASIC, lodged, or a problem we're handling.</summary>
    private static string PublicStage(BusinessNameRegistration r) => r.Status switch
    {
        BusinessNameRegistrationStatus.AwaitingPayment => "unpaid",
        BusinessNameRegistrationStatus.Queued or BusinessNameRegistrationStatus.Running => "lodging",
        BusinessNameRegistrationStatus.AwaitingAsicPayment or BusinessNameRegistrationStatus.Lodged => "lodged",
        BusinessNameRegistrationStatus.Refused or BusinessNameRegistrationStatus.Refunded => "refused",
        _ => r.LodgedAt is not null ? "lodged" : "lodging",
    };

    private static object ToDto(BusinessNameRegistration r) => new
    {
        id = r.Id,
        createdAt = r.CreatedAt,
        status = r.Status.ToString(),
        businessName = r.BusinessName,
        abn = r.Abn,
        years = r.Years,
        firstName = r.FirstName,
        lastName = r.LastName,
        email = r.Email,
        phone = r.Phone,
        serviceAddress = r.ServiceAddress.ToString(),
        principalAddress = r.PrincipalAddress.ToString(),
        amount = r.Amount,
        paidAt = r.PaidAt,
        stripePaymentIntentId = r.StripePaymentIntentId,
        attemptCount = r.AttemptCount,
        startedAt = r.StartedAt,
        finishedAt = r.FinishedAt,
        asicTransactionReference = r.AsicTransactionReference,
        abrEntityName = r.AbrEntityName,
        abrEntityType = r.AbrEntityType,
        nameVerdict = r.NameVerdict,
        nameVerdictText = r.NameVerdictText,
        asicPayBy = r.AsicPayBy,
        asicFee = r.AsicFee,
        lodgedAt = r.LodgedAt,
        // ASIC holds an unpaid application for 10 business days (about two weeks).
        payBy = r.LodgedAt is { } l && r.AsicPaidAt is null && r.AsicPayBy == "Bpay" ? AddBusinessDays(l, 10) : (DateTime?)null,
        bpayBillerCode = r.BpayBillerCode,
        bpayReference = r.BpayReference,
        airwallexTransferId = r.AirwallexTransferId,
        airwallexTransferStatus = r.AirwallexTransferStatus,
        bpayPaymentError = r.BpayPaymentError,
        asicPaidAt = r.AsicPaidAt,
        asicTransactionStatus = r.AsicTransactionStatus,
        asicPaymentStatus = r.AsicPaymentStatus,
        asicCheckedAt = r.AsicCheckedAt,
        failedAtStep = r.FailedAtStep,
        error = r.Error,
        isTest = r.IsTest,
    };

    private static DateTime AddBusinessDays(DateTime from, int days)
    {
        var d = from.Date;
        while (days > 0)
        {
            d = d.AddDays(1);
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days--;
        }
        return d;
    }
}
