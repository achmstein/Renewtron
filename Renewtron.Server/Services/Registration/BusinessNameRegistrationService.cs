using System.Text;
using System.Text.RegularExpressions;
using Asic.Client;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services.AsicPortal;
using Renewtron.Services.Email;
using Renewtron.Settings;

namespace Renewtron.Services.Registration;

/// <summary>What the public register page sends.</summary>
public sealed record RegistrationInput(
    string BusinessName,
    string Abn,
    int Years,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    RegistrationAddress ServiceAddress,
    RegistrationAddress? PrincipalAddress,
    string? VisitorId,
    string? Source);

/// <summary>The answer to a payment attempt: paid, 3D Secure needed (ClientSecret), or why not.</summary>
public sealed record RegistrationPayResult(bool Paid, bool RequiresAction, string? ClientSecret, string? Error);

/// <summary>
/// Registering a new business name for a customer: they apply and pay us on the public page, then
/// one asic-portal job fills ASIC Connect's "Apply for a Business Name" wizard with our agent login
/// and lodges it to pay later by BPAY; a person pays the BPAY bill through Airwallex from the admin
/// page, and a recurring check reads ASIC's myTransactions until the fee shows as paid.
/// </summary>
public interface IBusinessNameRegistrationService
{
    /// <summary>Whether our copy of the register already has this exact name.</summary>
    Task<bool> IsNameTakenLocallyAsync(string businessName, CancellationToken ct = default);

    Task<(BusinessNameRegistration? Registration, string? Problem)> CreateAsync(RegistrationInput input);

    /// <summary>Charges the customer (Stripe) and queues the ASIC job once paid.</summary>
    Task<RegistrationPayResult> PayAsync(Guid id, string paymentMethodId);

    /// <summary>After 3D Secure: checks the PaymentIntent with Stripe, then queues the ASIC job.</summary>
    Task<RegistrationPayResult> CompletePaymentAsync(Guid id, string paymentIntentId);

    /// <summary>
    /// Hangfire entry point: one application, one ASIC session. On the renewals' one-worker "asic"
    /// queue, because paying ASIC by card reads the same OTP inbox and uses the same card as a
    /// renewal; and it holds the ASIC Connect login lock against portal jobs.
    /// </summary>
    [Queue("asic")]
    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts = 0)]
    Task RunAsync(Guid id);

    /// <summary>Queues an application again (one that was refused, failed or needs a look, and was never lodged).</summary>
    Task<string?> RetryAsync(Guid id);

    /// <summary>Pays ASIC's BPAY bill through Airwallex. Null when sent; else why not.</summary>
    Task<string?> PayAsicByBpayAsync(Guid id, decimal expectedAmount);

    /// <summary>Marks the customer refunded (the refund itself is made in Stripe).</summary>
    Task<string?> MarkRefundedAsync(Guid id);

    /// <summary>
    /// Recurring: reads back BPAY transfers in flight, then ASIC's myTransactions for lodged
    /// applications, and marks them Lodged once ASIC shows the fee paid.
    /// </summary>
    [Queue(AsicPortalLinkService.Queue)]
    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts = 0)]
    Task CheckAsicAsync();
}

public class BusinessNameRegistrationService(
    ApplicationDbContext db,
    IOptionsMonitor<RegistrationSettings> registrationSettings,
    IOptionsMonitor<AsicPortalSettings> portalSettings,
    IOptionsMonitor<AsicSettings> asicSettings,
    IOptionsMonitor<TestModeSettings> testMode,
    IOptionsMonitor<AirwallexSettings> airwallexSettings,
    IStripePaymentService stripe,
    IAirwallexClient airwallex,
    IAsicPaymentClient asicPayment,
    ISmsProvider sms,
    IEmailService email,
    IBackgroundJobClient jobs,
    ILogger<BusinessNameRegistrationService> logger) : IBusinessNameRegistrationService
{
    /// <summary>
    /// How long we watch myTransactions after paying ASIC by card: ASIC says a card payment can take
    /// 2–3 minutes. Delays between reads, the first short.
    /// </summary>
    public static readonly TimeSpan[] CardCheckDelays =
        [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)];

    /// <summary>How long after lodging we keep reading ASIC's myTransactions for an unpaid fee.</summary>
    private static readonly TimeSpan WatchFor = TimeSpan.FromDays(21);

    public static readonly string[] States = ["ACT", "NSW", "NT", "QLD", "SA", "TAS", "VIC", "WA"];

    /* ───────────── The public page ───────────── */

    public async Task<bool> IsNameTakenLocallyAsync(string businessName, CancellationToken ct = default)
    {
        var name = CleanName(businessName);
        if (name.Length == 0) return false;
        // The register's collation ignores case; the (Name, ImportId) index makes this a seek.
        return await db.RegisteredBusinessNames.AsNoTracking()
            .Where(n => n.Name == name)
            .Where(n => db.BusinessNameImports.Any(i => i.Id == n.ImportId && i.IsActive))
            .AnyAsync(ct);
    }

    public async Task<(BusinessNameRegistration? Registration, string? Problem)> CreateAsync(RegistrationInput input)
    {
        var settings = registrationSettings.CurrentValue;
        if (!settings.Enabled) return (null, "Registering new business names isn't open yet.");

        var name = CleanName(input.BusinessName);
        var abn = Modules.Helpers.NormalizeAbn(input.Abn);
        var problem = name.Length is < 2 or > 200 ? "Enter the business name you'd like to register."
            : !Modules.Helpers.IsValidAbn(abn) || !AbnChecksumOk(abn) ? "Enter a valid 11-digit ABN."
            : input.Years is not (1 or 3) ? "Choose 1 or 3 years."
            : string.IsNullOrWhiteSpace(input.FirstName) || string.IsNullOrWhiteSpace(input.LastName) ? "Enter your first and last name."
            : !Regex.IsMatch(input.Email?.Trim() ?? "", @"^[^@\s]+@[^@\s]+\.[^@\s]+$") ? "Enter a valid email address."
            : Regex.Replace(input.Phone ?? "", @"\D", "").Length < 8 ? "Enter a phone number."
            : AddressProblem(input.ServiceAddress, "address for service")
              ?? (input.PrincipalAddress is null ? null : AddressProblem(input.PrincipalAddress, "principal place of business"));
        if (problem is not null) return (null, problem);

        var price = settings.PriceFor(input.Years);
        if (price is null) return (null, "Registering new business names isn't open yet.");

        var reg = new BusinessNameRegistration
        {
            Id = Guid.NewGuid(),
            BusinessName = name,
            Abn = abn,
            Years = input.Years,
            FirstName = input.FirstName.Trim(),
            LastName = input.LastName.Trim(),
            Email = input.Email!.Trim(),
            Phone = input.Phone!.Trim(),
            ServiceAddress = Clean(input.ServiceAddress),
            PrincipalAddress = Clean(input.PrincipalAddress ?? input.ServiceAddress),
            Amount = price.Value,
            NameTakenLocally = await IsNameTakenLocallyAsync(name),
            IsTest = testMode.CurrentValue.IsTestEmail(input.Email),
            VisitorId = Trim(input.VisitorId, 64),
            Source = Trim(input.Source, 100),
        };
        if (reg.NameTakenLocally)
            return (null, $"\"{name}\" is already registered. Try a different name.");

        db.BusinessNameRegistrations.Add(reg);
        await db.SaveChangesAsync();
        return (reg, null);
    }

    public async Task<RegistrationPayResult> PayAsync(Guid id, string paymentMethodId)
    {
        var reg = await db.BusinessNameRegistrations.FirstOrDefaultAsync(r => r.Id == id);
        if (reg is null) return Failed("Application not found.");
        if (reg.PaidAt is not null) return new(true, false, null, null);
        if (reg.Status != BusinessNameRegistrationStatus.AwaitingPayment) return Failed("This application can't be paid for now.");
        if (reg.IsTest && !testMode.CurrentValue.StripeReady)
            return Failed("Test mode is missing its Stripe test keys (Admin → Settings → Test mode).");

        // Same order, same key: a double click or a retry never charges twice.
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"registration:{reg.Id}:{reg.Amount}:{reg.Email}")));
        var confirm = await stripe.ConfirmPaymentAsync(reg.Amount, reg.Email,
            $"Business Name Registration - {reg.BusinessName} ({reg.Years} year{(reg.Years == 1 ? "" : "s")})",
            new Dictionary<string, string>
            {
                ["registration_id"] = reg.Id.ToString(),
                ["business_name"] = reg.BusinessName,
                ["abn"] = reg.Abn,
                ["years"] = reg.Years.ToString(),
            },
            paymentMethodId, key, reg.IsTest);

        if (confirm.RequiresAction) return new(false, true, confirm.ClientSecret, null);
        if (!confirm.Success) return Failed(confirm.ErrorMessage ?? "Payment failed.");
        await MarkPaidAsync(reg, confirm.PaymentIntentId!);
        return new(true, false, null, null);
    }

    public async Task<RegistrationPayResult> CompletePaymentAsync(Guid id, string paymentIntentId)
    {
        var reg = await db.BusinessNameRegistrations.FirstOrDefaultAsync(r => r.Id == id);
        if (reg is null) return Failed("Application not found.");
        if (reg.PaidAt is not null) return new(true, false, null, null);

        // Never take the browser's word: the intent must be ours, for this application, paid in full.
        var intent = await stripe.GetPaymentIntentAsync(paymentIntentId, reg.IsTest);
        if (intent is null) return Failed("Payment not found.");
        if (!intent.Metadata.TryGetValue("registration_id", out var rid) || rid != reg.Id.ToString())
            return Failed("That payment isn't for this application.");
        if (intent.AmountInCents != (long)Math.Round(reg.Amount * 100m)) return Failed("The amount paid doesn't match.");
        if (intent.Status == "requires_action") return Failed("The card still needs to be verified.");
        if (intent.Status != "succeeded") return Failed("The payment didn't go through. Please try another card.");

        await MarkPaidAsync(reg, intent.PaymentIntentId);
        return new(true, false, null, null);
    }

    private async Task MarkPaidAsync(BusinessNameRegistration reg, string paymentIntentId)
    {
        reg.StripePaymentIntentId = paymentIntentId;
        reg.PaidAt = DateTime.UtcNow;
        reg.Status = BusinessNameRegistrationStatus.Queued;
        await db.SaveChangesAsync();
        jobs.Enqueue<IEmailService>(e => e.SendRegistrationEmailAsync(EmailTemplateCatalog.RegistrationReceived, reg.Id, null));
        jobs.Enqueue<IBusinessNameRegistrationService>(s => s.RunAsync(reg.Id));
        logger.LogInformation("Registration {Id} for {Name} paid ({Intent}); queued for ASIC", reg.Id, reg.BusinessName, paymentIntentId);
    }

    /* ───────────── The ASIC job ───────────── */

    public async Task RunAsync(Guid id)
    {
        var reg = await db.BusinessNameRegistrations.FirstOrDefaultAsync(r => r.Id == id);
        if (reg is null || reg.Status is not (BusinessNameRegistrationStatus.Queued or BusinessNameRegistrationStatus.Running)) return;
        if (reg.LodgedAt is not null) return; // never lodge the same application twice

        var portal = portalSettings.CurrentValue;
        var settings = registrationSettings.CurrentValue;
        // A test customer's application is always a dry run: filled in at ASIC, then cancelled.
        var lodge = settings.Lodge && !reg.IsTest;

        // Card or BPAY, decided the way renewals decide it.
        var (payBy, payNote) = lodge ? await ChoosePayByAsync() : (AsicPayBy.Bpay, null);

        reg.Status = BusinessNameRegistrationStatus.Running;
        reg.StartedAt = DateTime.UtcNow;
        reg.FinishedAt = null;
        reg.AttemptCount++;
        reg.Error = null;
        reg.FailedAtStep = null;
        await db.SaveChangesAsync();

        using var session = AsicConnectSession.Acquire();
        using var client = new AsicPortalClient();
        try
        {
            if (!portal.HasLogin) { NeedsLook(reg, "Login", "No ASIC Connect login in Settings (ASIC portal)."); return; }
            var login = await client.LoginAsync(portal.Username, portal.Password);
            if (!login.Ok) { NeedsLook(reg, "Login", login.Error ?? "ASIC Connect refused the login."); return; }

            var outcome = await client.RegisterBusinessNameAsync(new BnRegistrationRequest(
                reg.Abn, reg.BusinessName,
                reg.Years == 3 ? BnRegistrationPeriod.ThreeYears : BnRegistrationPeriod.OneYear,
                ToAsic(reg.ServiceAddress), ToAsic(reg.PrincipalAddress),
                reg.Email, reg.Phone, payBy, settings.AllowManualDecision), lodge);

            Apply(reg, outcome);
            if (outcome.Lodged) reg.AsicFee ??= asicSettings.CurrentValue.FeeFor(reg.Years);
            await db.SaveChangesAsync();
            if (outcome.Stage == BnRegistrationStage.AwaitingBpay && payNote is not null)
                reg.Error = Trim($"Paying by BPAY: {payNote}", 2000);
            if (outcome.Stage == BnRegistrationStage.AwaitingCardPayment && outcome.PaymentUrl is not null)
                await PayByCardAsync(client, reg, outcome.PaymentUrl);
            await db.SaveChangesAsync();
            await AfterRunAsync(reg, outcome);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Registration {Id} for {Name} failed", reg.Id, reg.BusinessName);
            // An exception after Declare could mean it's lodged: a person checks myTransactions before any retry.
            NeedsLook(reg, reg.FailedAtStep ?? "Run", ex.Message);
        }
        finally
        {
            try { await client.LogoutAsync(); } catch { /* best effort */ }
            reg.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            logger.LogInformation("Registration {Id} for {Name}: {Status} (ref {Ref}, verdict {Verdict}) {Error}",
                reg.Id, reg.BusinessName, reg.Status, reg.AsicTransactionReference, reg.NameVerdict, reg.Error);
        }
    }

    /// <summary>Copies what ASIC said onto the application and sets its status.</summary>
    public static void Apply(BusinessNameRegistration reg, BnRegistrationOutcome o)
    {
        reg.AsicTransactionReference = o.TransactionReference ?? reg.AsicTransactionReference;
        reg.AbrEntityName = o.AbrEntityName ?? reg.AbrEntityName;
        reg.AbrEntityType = o.AbrEntityType ?? reg.AbrEntityType;
        if (o.Verdict != BnNameVerdict.Unknown || reg.NameVerdict is null) reg.NameVerdict = o.Verdict.ToString();
        reg.NameVerdictText = Trim(o.VerdictText, 1000) ?? reg.NameVerdictText;
        reg.PageText = Trim(o.PageText, 20_000);
        reg.FailedAtStep = o.FailedAtStep;
        reg.Error = Trim(o.Problem, 2000);

        if (o.Lodged) reg.LodgedAt ??= DateTime.UtcNow;
        switch (o.Stage)
        {
            case BnRegistrationStage.DryRunOk:
                reg.Status = BusinessNameRegistrationStatus.NeedsLook;
                reg.Error = "Dry run: everything was filled in at ASIC and reviewed, then cancelled (lodging is off in Settings, or this is a test customer).";
                break;
            case BnRegistrationStage.NotLodged:
                // ASIC said no to the name or the ABN: the customer is refunded. Anything else is ours to look at.
                var refused = o.FailedAtStep == "ABN"
                              || (o.FailedAtStep == "Name" && o.Verdict is BnNameVerdict.NotAvailable or BnNameVerdict.ManualDecision);
                reg.Status = refused ? BusinessNameRegistrationStatus.Refused : BusinessNameRegistrationStatus.NeedsLook;
                break;
            case BnRegistrationStage.AwaitingBpay:
                reg.AsicPayBy = "Bpay";
                reg.BpayBillerCode = o.BpayBillerCode;
                reg.BpayReference = o.BpayReference;
                reg.Status = BusinessNameRegistrationStatus.AwaitingAsicPayment;
                break;
            case BnRegistrationStage.AwaitingCardPayment:
                // Not used yet (registrations pay ASIC by BPAY), but never lose the fact it's lodged.
                reg.AsicPayBy = "Card";
                reg.Status = BusinessNameRegistrationStatus.NeedsLook;
                reg.Error ??= "Lodged to pay by card, which registrations don't do yet: pay or resume it in ASIC Connect.";
                break;
            case BnRegistrationStage.LodgedForReview:
                reg.Status = BusinessNameRegistrationStatus.Lodged;
                break;
            case BnRegistrationStage.LodgedUnclear:
                reg.Status = BusinessNameRegistrationStatus.NeedsLook;
                reg.Error ??= "Lodged, but we couldn't read ASIC's answer. Check myTransactions in ASIC Connect.";
                break;
        }
        if (o.Stage == BnRegistrationStage.AwaitingBpay && o.Problem is not null)
            reg.Error = Trim(o.Problem + " The BPAY details were read; check the transaction in ASIC Connect.", 2000);
    }

    private async Task AfterRunAsync(BusinessNameRegistration reg, BnRegistrationOutcome outcome)
    {
        if (reg.Status == BusinessNameRegistrationStatus.Refused)
            jobs.Enqueue<IEmailService>(e => e.SendRegistrationEmailAsync(EmailTemplateCatalog.RegistrationRefused, reg.Id, outcome.Problem));
        else if (outcome.Lodged && reg.AsicTransactionReference is not null)
            jobs.Enqueue<IEmailService>(e => e.SendRegistrationEmailAsync(EmailTemplateCatalog.RegistrationLodged, reg.Id, null));
        await Task.CompletedTask;
    }

    /// <summary>
    /// Card unless Settings pay ASIC by BPAY, there's no card, the card breaker is down (ASIC declined
    /// it on the last few renewals), or the OTP inbox can't be read (a card payment needs its OTP).
    /// Unlike a renewal, a registration never waits for the OTP: BPAY needs none.
    /// </summary>
    private async Task<(AsicPayBy By, string? Note)> ChoosePayByAsync()
    {
        var s = asicSettings.CurrentValue;
        if (AsicPaymentMethods.IsBpay(s.PaymentMethod)) return (AsicPayBy.Bpay, null);
        if (string.IsNullOrWhiteSpace(s.CardNumber)) return (AsicPayBy.Bpay, "there's no ASIC card in Settings.");
        if (s.BpayAfterCardFailures > 0 && await AsicCardHealth.CheckAsync(db, DateTime.UtcNow) is { Down: true } card)
            return (AsicPayBy.Bpay, $"ASIC declined our card on the last {card.Declines} card payments.");
        if (await sms.CheckAsync() is { } otp) return (AsicPayBy.Bpay, $"the card's OTP can't be read right now ({otp}).");
        return (AsicPayBy.Card, null);
    }

    /// <summary>
    /// Pays ASIC's card window in this session, then reads myTransactions for what ASIC made of it
    /// (the payment client said "success" for a card ASIC declined, 11 Nov 2026). Declined: the
    /// transaction is resumed and switched to BPAY, as renewals fall back. Anything unclear stops for
    /// a person: paying again could pay twice.
    /// </summary>
    private async Task PayByCardAsync(AsicPortalClient client, BusinessNameRegistration reg, string paymentUrl)
    {
        var s = asicSettings.CurrentValue;
        reg.AsicPayBy = "Card";
        reg.Status = BusinessNameRegistrationStatus.AwaitingAsicPayment;
        reg.Error = null;
        reg.FailedAtStep = null;
        await db.SaveChangesAsync();

        string payerSays;
        try
        {
            var paid = await asicPayment.ProcessPaymentAsync(paymentUrl, reg.AsicTransactionReference!, new CreditCardDetails
            {
                CardNumber = s.CardNumber,
                CardholderName = s.CardholderName,
                ExpiryMonth = s.ExpiryMonth,
                ExpiryYear = s.ExpiryYear,
                Cvc = s.Cvc,
            });
            payerSays = paid.Message ?? (paid.Success ? "paid" : "failed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Registration {Id}: card payment threw", reg.Id);
            payerSays = ex.Message;
        }

        AsicTransactionRow? row = null;
        foreach (var delay in CardCheckDelays)
        {
            await Task.Delay(delay);
            row = (await client.GetRecentTransactionsAsync())?
                .FirstOrDefault(r => r.Reference.Equals(reg.AsicTransactionReference, StringComparison.OrdinalIgnoreCase));
            if (row is not null && (IsPaid(row.PaymentStatus) || IsDeclined(row.PaymentStatus))) break;
        }
        reg.AsicCheckedAt = DateTime.UtcNow;
        reg.AsicTransactionStatus = Trim(row?.TransactionStatus, 40);
        reg.AsicPaymentStatus = Trim(row?.PaymentStatus, 40);

        if (row is not null && IsPaid(row.PaymentStatus))
        {
            reg.AsicPaidAt = DateTime.UtcNow;
            reg.Status = BusinessNameRegistrationStatus.Lodged;
            logger.LogInformation("Registration {Id}: ASIC's fee paid by card", reg.Id);
            return;
        }

        if (row is not null && IsDeclined(row.PaymentStatus))
        {
            logger.LogWarning("Registration {Id}: ASIC declined our card ({Payer}); switching to BPAY", reg.Id, payerSays);
            var bpay = await client.ResumeAndPayLaterByBpayAsync(reg.AsicTransactionReference!);
            if (bpay.Biller is not null && bpay.Reference is not null)
            {
                reg.AsicPayBy = "Bpay";
                reg.BpayBillerCode = bpay.Biller;
                reg.BpayReference = bpay.Reference;
                reg.Status = BusinessNameRegistrationStatus.AwaitingAsicPayment;
                reg.Error = Trim("ASIC declined our card, so it was switched to pay later by BPAY." +
                                 (bpay.Problem is null ? "" : $" {bpay.Problem}"), 2000);
                reg.PageText = Trim(bpay.Text, 20_000) ?? reg.PageText;
                return;
            }
            NeedsLook(reg, "Payment", $"ASIC declined our card, and switching it to BPAY didn't work: {bpay.Problem}");
            reg.PageText = Trim(bpay.Text, 20_000) ?? reg.PageText;
            return;
        }

        NeedsLook(reg, "Payment",
            $"Paid ASIC by card, but ASIC's myTransactions doesn't say whether it went through (payment status \"{row?.PaymentStatus}\"; " +
            $"the payment page said: {payerSays}). Check {reg.AsicTransactionReference} in ASIC Connect before paying again.");
    }

    /// <summary>myTransactions' payment status words for a refused card.</summary>
    public static bool IsDeclined(string? paymentStatus) =>
        paymentStatus is not null && Regex.IsMatch(paymentStatus, @"declin|reject|fail", RegexOptions.IgnoreCase);

    private static void NeedsLook(BusinessNameRegistration reg, string step, string error)
    {
        reg.Status = BusinessNameRegistrationStatus.NeedsLook;
        reg.FailedAtStep = step;
        reg.Error = Trim(error, 2000);
    }

    public async Task<string?> RetryAsync(Guid id)
    {
        var reg = await db.BusinessNameRegistrations.FirstOrDefaultAsync(r => r.Id == id);
        if (reg is null) return "Application not found.";
        if (reg.PaidAt is null) return "The customer hasn't paid for this application.";
        if (reg.LodgedAt is not null) return "This application is already lodged with ASIC; it can't be lodged again.";
        if (reg.Status is BusinessNameRegistrationStatus.Queued or BusinessNameRegistrationStatus.Running) return "It's already queued.";
        if (reg.Status == BusinessNameRegistrationStatus.Refunded) return "The customer was refunded.";
        reg.Status = BusinessNameRegistrationStatus.Queued;
        reg.Error = null;
        reg.FailedAtStep = null;
        await db.SaveChangesAsync();
        jobs.Enqueue<IBusinessNameRegistrationService>(s => s.RunAsync(reg.Id));
        return null;
    }

    public async Task<string?> MarkRefundedAsync(Guid id)
    {
        var reg = await db.BusinessNameRegistrations.FirstOrDefaultAsync(r => r.Id == id);
        if (reg is null) return "Application not found.";
        if (reg.LodgedAt is not null && reg.Status != BusinessNameRegistrationStatus.NeedsLook)
            return "This application is lodged with ASIC.";
        reg.Status = BusinessNameRegistrationStatus.Refunded;
        await db.SaveChangesAsync();
        return null;
    }

    /* ───────────── Paying ASIC by BPAY (Airwallex) ───────────── */

    public async Task<string?> PayAsicByBpayAsync(Guid id, decimal expectedAmount)
    {
        if (!airwallexSettings.CurrentValue.Ready) return "Airwallex isn't set up (Settings → Airwallex).";
        var reg = await db.BusinessNameRegistrations.FirstOrDefaultAsync(r => r.Id == id);
        if (reg is null) return "Application not found.";
        if (reg.IsTest) return "Test applications are never paid.";
        if (string.IsNullOrEmpty(reg.BpayBillerCode) || string.IsNullOrEmpty(reg.BpayReference)) return "There's no BPAY bill for this application.";
        if (reg.AsicPaidAt is not null) return "ASIC's fee is already paid.";
        if (reg.AirwallexTransferStatus is { } st && !BpayPaymentService.Ended.Contains(st) && st != BpayPaymentService.Unknown)
            return "A payment is already on its way.";
        var fee = asicSettings.CurrentValue.FeeFor(reg.Years);
        if (fee is null) return $"ASIC's {reg.Years}-year fee isn't set (Settings → ASIC).";
        if (fee.Value != expectedAmount) return $"ASIC's fee is now A${fee:F2}, not A${expectedAmount:F2}. Refresh and check.";

        if (reg.AirwallexTransferStatus != BpayPaymentService.Unknown) reg.BpayPaymentAttempts++;
        var requestId = $"bpay-reg-{reg.Id:N}-{reg.BpayPaymentAttempts}";

        if (reg.AirwallexTransferStatus == BpayPaymentService.Unknown)
        {
            var found = await airwallex.FindTransferByRequestIdAsync(requestId);
            if (found.Error is not null) return await RecordBpay(reg, null, BpayPaymentService.Unknown, $"Couldn't check the earlier payment: {found.Error}");
            if (found.Transfer is { } existing) return await RecordBpay(reg, existing, existing.Status, existing.FailureReason);
        }

        reg.AsicFee = fee;
        reg.AirwallexTransferStatus = BpayPaymentService.Unknown; // on record before sending
        reg.BpayPaymentError = null;
        await db.SaveChangesAsync();

        var result = await airwallex.CreateBpayTransferAsync(requestId, reg.BpayBillerCode, reg.BpayReference, fee.Value,
            Trim($"ASIC {reg.AsicTransactionReference ?? reg.BusinessName}", 18)!);
        if (result.Transfer is { } transfer) return await RecordBpay(reg, transfer, transfer.Status, transfer.FailureReason);
        return await RecordBpay(reg, null, result.Uncertain ? BpayPaymentService.Unknown : BpayPaymentService.Rejected, result.Error);
    }

    private async Task<string?> RecordBpay(BusinessNameRegistration reg, AirwallexTransfer? transfer, string status, string? error)
    {
        if (transfer is not null && !string.IsNullOrEmpty(transfer.Id)) reg.AirwallexTransferId = transfer.Id;
        ApplyBpay(reg, status, error);
        await db.SaveChangesAsync();
        logger.LogInformation("BPAY for registration {Id}: {Status} (transfer {Transfer}) {Error}", reg.Id, status, reg.AirwallexTransferId, error);
        return status is BpayPaymentService.Rejected or BpayPaymentService.Unknown ? error ?? "Airwallex didn't take the payment." : null;
    }

    private static void ApplyBpay(BusinessNameRegistration reg, string status, string? error)
    {
        reg.AirwallexTransferStatus = status;
        reg.BpayPaymentError = BpayPaymentService.Ended.Contains(status) || status == BpayPaymentService.Unknown ? Trim(error, 1000) : null;
        if (status == BpayPaymentService.Paid)
        {
            // Sent: ASIC's myTransactions catches up when BPAY clears (a business day or two).
            reg.AsicPaidAt ??= DateTime.UtcNow;
            if (reg.Status == BusinessNameRegistrationStatus.AwaitingAsicPayment) reg.Status = BusinessNameRegistrationStatus.Lodged;
        }
        else if (BpayPaymentService.Ended.Contains(status) && reg.AirwallexTransferId is not null)
        {
            // Includes a PAID transfer that the clearing system bounced later.
            reg.AsicPaidAt = null;
            if (reg.Status == BusinessNameRegistrationStatus.Lodged && reg.AsicPayBy == "Bpay") reg.Status = BusinessNameRegistrationStatus.AwaitingAsicPayment;
        }
    }

    /* ───────────── Checking back ───────────── */

    public async Task CheckAsicAsync()
    {
        var since = DateTime.UtcNow - WatchFor;
        var open = await db.BusinessNameRegistrations
            .Where(r => r.LodgedAt != null && r.LodgedAt >= since && !r.IsTest)
            .Where(r => r.Status == BusinessNameRegistrationStatus.AwaitingAsicPayment
                        || r.Status == BusinessNameRegistrationStatus.Lodged && r.AsicPayBy == "Bpay" && r.AsicPaymentStatus != "paid"
                        || (r.AirwallexTransferStatus != null && r.AsicPaidAt == null))
            .ToListAsync();
        if (open.Count == 0) return;

        // BPAY transfers first: Airwallex's own status.
        foreach (var reg in open.Where(r => r.AirwallexTransferStatus is { } s && !BpayPaymentService.Ended.Contains(s)))
        {
            var result = reg.AirwallexTransferId is { } tid
                ? await airwallex.GetTransferAsync(tid)
                : await airwallex.FindTransferByRequestIdAsync($"bpay-reg-{reg.Id:N}-{reg.BpayPaymentAttempts}");
            if (result.Transfer is { } t)
            {
                reg.AirwallexTransferId = t.Id;
                ApplyBpay(reg, t.Status, t.FailureReason);
            }
        }
        await db.SaveChangesAsync();

        // Then ASIC: myTransactions shows the fee's payment status once BPAY clears (a day or two).
        var portal = portalSettings.CurrentValue;
        if (!portal.HasLogin) return;
        using var session = AsicConnectSession.Acquire();
        using var client = new AsicPortalClient();
        try
        {
            var login = await client.LoginAsync(portal.Username, portal.Password);
            if (!login.Ok) { logger.LogWarning("Registration check: {Error}", login.Error); return; }
            var rows = await client.GetRecentTransactionsAsync() ?? [];
            foreach (var reg in open.Where(r => r.AsicTransactionReference is not null))
            {
                var row = rows.FirstOrDefault(x => x.Reference.Equals(reg.AsicTransactionReference, StringComparison.OrdinalIgnoreCase));
                reg.AsicCheckedAt = DateTime.UtcNow;
                if (row is null) continue; // finished transactions drop off the recent list
                reg.AsicTransactionStatus = Trim(row.TransactionStatus, 40);
                reg.AsicPaymentStatus = Trim(row.PaymentStatus, 40);
                if (IsPaid(row.PaymentStatus))
                {
                    reg.AsicPaidAt ??= DateTime.UtcNow;
                    reg.Status = BusinessNameRegistrationStatus.Lodged;
                }
            }
            await db.SaveChangesAsync();
        }
        finally
        {
            try { await client.LogoutAsync(); } catch { /* best effort */ }
        }
    }

    /// <summary>myTransactions' payment status words for a paid fee.</summary>
    public static bool IsPaid(string? paymentStatus) =>
        paymentStatus is not null && Regex.IsMatch(paymentStatus, @"^\s*(paid|approved|successful|complete[d]?)\s*$", RegexOptions.IgnoreCase);

    /* ───────────── Helpers ───────────── */

    private static RegistrationPayResult Failed(string error) => new(false, false, null, error);

    public static string CleanName(string? name) => Regex.Replace(name ?? "", @"\s+", " ").Trim().ToUpperInvariant();

    private static AsicPortalClient.HolderAddress ToAsic(RegistrationAddress a) =>
        new(a.Unit, a.StreetNumber, a.StreetName, a.StreetType, a.Suburb, a.State, a.Postcode);

    private static RegistrationAddress Clean(RegistrationAddress a) => new()
    {
        Unit = (a.Unit ?? "").Trim(),
        StreetNumber = (a.StreetNumber ?? "").Trim(),
        StreetName = Regex.Replace(a.StreetName ?? "", @"\s+", " ").Trim(),
        StreetType = (a.StreetType ?? "").Trim(),
        Suburb = Regex.Replace(a.Suburb ?? "", @"\s+", " ").Trim(),
        State = (a.State ?? "").Trim().ToUpperInvariant(),
        Postcode = (a.Postcode ?? "").Trim(),
    };

    public static string? AddressProblem(RegistrationAddress? a, string what)
    {
        if (a is null) return $"Enter the {what}.";
        if (string.IsNullOrWhiteSpace(a.StreetNumber) || string.IsNullOrWhiteSpace(a.StreetName) || string.IsNullOrWhiteSpace(a.StreetType))
            return $"Enter the street number, name and type of the {what}.";
        if (string.IsNullOrWhiteSpace(a.Suburb)) return $"Enter the suburb of the {what}.";
        if (!States.Contains((a.State ?? "").Trim().ToUpperInvariant())) return $"Choose the state of the {what}.";
        if (!Regex.IsMatch(a.Postcode ?? "", @"^\s*\d{4}\s*$")) return $"Enter the 4-digit postcode of the {what}.";
        if ((a.StreetName + a.Suburb).Contains('"')) return $"The {what} can't contain double quotation marks.";
        return null;
    }

    /// <summary>The ABR's ABN check: weights 10,1,3,…,19 over the digits (first minus one), sum divisible by 89.</summary>
    public static bool AbnChecksumOk(string abn)
    {
        if (abn.Length != 11 || !abn.All(char.IsDigit)) return false;
        int[] weights = [10, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19];
        var sum = 0;
        for (var i = 0; i < 11; i++)
            sum += ((abn[i] - '0') - (i == 0 ? 1 : 0)) * weights[i];
        return sum % 89 == 0;
    }

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
