using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Tests;

/// <summary>The "what happened" story every record page of a journey shares.</summary>
public class JourneyTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 23, 0, 0, DateTimeKind.Utc); // 10:00 on 4 Nov in Sydney

    private sealed record Ids(Guid Lead, Guid Search, Guid FirstRun, Guid RetryRun, Guid Order, Guid OtherLead);

    /// <summary>
    /// Jane looks up her ABN (two names), gives her details, pays by card for one name. The first
    /// ASIC run is declined; the retry lodges it to pay by BPAY and the bill is paid. She is
    /// emailed. Someone else's lookup on another ABN happens the same day.
    /// </summary>
    private static (ApplicationDbContext Db, Ids Ids) Seed()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var search = new SearchLog { Id = Guid.NewGuid(), Abn = "49642383462", SearchedAt = T0, Success = true, ResultsCount = 2, InitiatedBy = SearchInitiator.Customer, Source = SearchSource.Local };
        var smith = new SearchResult { Id = Guid.NewGuid(), SearchLogId = search.Id, SearchLog = search, BusinessName = "SMITH PLUMBING", AccountNumber = "1", RegistrationDate = "01/01/2020", IsAvailable = true };
        var other = new SearchResult { Id = Guid.NewGuid(), SearchLogId = search.Id, SearchLog = search, BusinessName = "SMITH GAS", AccountNumber = "2", RegistrationDate = "01/01/2021", IsAvailable = true };
        var lead = new Lead
        {
            Id = Guid.NewGuid(), Abn = search.Abn, FullName = "Jane Citizen", Email = "jane@example.com", MobileNumber = "0400000000",
            CreatedAt = T0.AddMinutes(1), NamesShownAt = T0.AddMinutes(2), ReachedPaymentAt = T0.AddMinutes(3), SearchLogId = search.Id,
        };
        var order = new RenewalOrder { Id = Guid.NewGuid(), Source = RenewalOrderSource.Wizard, Abn = search.Abn, BusinessName = "SMITH PLUMBING", NameKey = "SMITHPLUMBING", Years = 1, AmountPaid = 99, PaidAt = T0.AddMinutes(4), Stage = RenewalOrderStage.Renewed };
        var first = new RenewalRequest
        {
            Id = Guid.NewGuid(), SearchResultId = smith.Id, SearchResult = smith, LeadId = lead.Id, RenewalOrderId = order.Id, InitiatedAt = T0.AddMinutes(5),
            RenewalYears = 1, Amount = 99, PaymentType = PaymentType.Stripe, Status = RenewalStatus.Failed, FailedAtStep = "Complete Payment Action",
            ErrorMessage = "Card declined at ASIC", LastAttemptAt = T0.AddMinutes(6), HandledAt = T0.AddDays(1), HandledNote = "Retried by BPAY",
        };
        first.StripePayment = new StripePayment { Id = Guid.NewGuid(), RenewalRequestId = first.Id, PaymentIntentId = "pi_1", PaymentStatus = "succeeded", PaidAt = T0.AddMinutes(4), CardBrand = "visa", CardLast4 = "4242" };
        // Each ASIC run has its own lookup: the retry looked the ABN up again.
        var retrySearch = new SearchLog { Id = Guid.NewGuid(), Abn = search.Abn, SearchedAt = T0.AddDays(1).AddSeconds(30), Success = true, ResultsCount = 1, InitiatedBy = SearchInitiator.System, Source = SearchSource.Local };
        var retryName = new SearchResult { Id = Guid.NewGuid(), SearchLogId = retrySearch.Id, SearchLog = retrySearch, BusinessName = "SMITH PLUMBING", AccountNumber = "1", RegistrationDate = "01/01/2020", IsAvailable = true };
        var retry = new RenewalRequest
        {
            Id = Guid.NewGuid(), SearchResultId = retryName.Id, SearchResult = retryName, RenewalOrderId = order.Id, InitiatedAt = T0.AddDays(1).AddMinutes(1),
            RenewalYears = 1, Amount = 99, PaymentType = PaymentType.Stripe, Status = RenewalStatus.Completed, CompletedAt = T0.AddDays(1).AddMinutes(2),
            BpayBillerCode = "17301", BpayReference = "8596799759492", BpayPaidAt = T0.AddDays(1).AddMinutes(20), BpayAmount = 54,
        };
        order.SourceId = first.Id;
        var email = new OutboundMessage { Id = Guid.NewGuid(), LeadId = lead.Id, Channel = OutboundChannel.Email, Template = "receipt", Subject = "Your renewal receipt", Recipient = lead.Email, Status = OutboundStatus.Sent, CreatedAt = T0.AddMinutes(4), SentAt = T0.AddMinutes(4).AddSeconds(30) };

        var elsewhereSearch = new SearchLog { Id = Guid.NewGuid(), Abn = "11111111111", SearchedAt = T0.AddMinutes(2), Success = true, InitiatedBy = SearchInitiator.Customer, Source = SearchSource.Local };
        var otherLead = new Lead { Id = Guid.NewGuid(), Abn = "11111111111", FullName = "Someone Else", Email = "x@example.com", MobileNumber = "0", CreatedAt = T0.AddMinutes(3), SearchLogId = elsewhereSearch.Id };

        db.AddRange(search, smith, other, lead, order, first, retrySearch, retryName, retry, email, elsewhereSearch, otherLead);
        db.SaveChanges();
        return (db, new Ids(lead.Id, search.Id, first.Id, retry.Id, order.Id, otherLead.Id));
    }

    private static Task<Journey?> Journey(ApplicationDbContext db, JourneyRecord kind, Guid id) =>
        new JourneyService(db).ForAsync(kind, id, CancellationToken.None);

    [Fact]
    public async Task Every_record_of_the_journey_tells_the_same_story()
    {
        var (db, ids) = Seed();
        var fromLead = (await Journey(db, JourneyRecord.Lead, ids.Lead))!.Events;

        foreach (var (kind, id) in new[] { (JourneyRecord.Search, ids.Search), (JourneyRecord.Run, ids.FirstRun), (JourneyRecord.Run, ids.RetryRun), (JourneyRecord.Order, ids.Order) })
            Assert.Equal(fromLead, (await Journey(db, kind, id))!.Events);
    }

    [Fact]
    public async Task The_story_reads_in_order()
    {
        var (db, ids) = Seed();
        var texts = (await Journey(db, JourneyRecord.Order, ids.Order))!.Events.Select(e => e.Text).ToList();

        Assert.Equal(
        [
            "The visitor looked up ABN 49642383462 at ASIC: 2 names: SMITH PLUMBING, SMITH GAS",
            "Jane Citizen gave their details for ABN 49642383462 (jane@example.com)",
            "Jane saw the names to renew",
            "Jane opened the payment page",
            "Paid $99.00 by card (visa ending 4242) to renew SMITH PLUMBING for 1 year",
            "Emailed jane@example.com \"Your renewal receipt\"",
            "Renewtron started renewing SMITH PLUMBING at ASIC",
            "Renewing SMITH PLUMBING at ASIC failed at \"Complete Payment Action\"",
            "Staff marked the ASIC run handled",
            "Renewtron looked up ABN 49642383462 at ASIC: 1 name: SMITH PLUMBING",
            "Renewtron started renewing SMITH PLUMBING at ASIC",
            "ASIC took the renewal of SMITH PLUMBING, to be paid by BPAY",
            "Paid ASIC's BPAY bill for SMITH PLUMBING: $54.00",
        ], texts);
    }

    [Fact]
    public async Task Each_line_names_its_record_and_how_it_reads()
    {
        var (db, ids) = Seed();
        var events = (await Journey(db, JourneyRecord.Lead, ids.Lead))!.Events;

        var declined = Assert.Single(events, e => e.Text.Contains("failed"));
        Assert.Equal((JourneyRecord.Run, ids.FirstRun, "quiet", "Card declined at ASIC"), (declined.Record, declined.RecordId, declined.Meaning, declined.Detail)); // handled since
        var bpay = Assert.Single(events, e => e.Text.StartsWith("ASIC took"));
        Assert.Equal((ids.RetryRun, "waiting", "Biller 17301, reference 8596799759492"), (bpay.RecordId, bpay.Meaning, bpay.Detail));
        Assert.All(events, e => Assert.Equal(DateTimeKind.Utc, e.At.Kind));
    }

    [Fact]
    public async Task Someone_elses_lookup_is_another_story()
    {
        var (db, ids) = Seed();

        Assert.DoesNotContain((await Journey(db, JourneyRecord.Lead, ids.Lead))!.Events, e => e.Text.Contains("Someone Else") || e.Text.Contains("11111111111"));
        var theirs = (await Journey(db, JourneyRecord.Lead, ids.OtherLead))!.Events;
        Assert.Equal(2, theirs.Count);
    }

    [Fact]
    public async Task An_ontraport_sale_leads_to_its_renewal_and_back()
    {
        var (db, _) = Seed();
        var sale = new OntraportSale
        {
            Id = Guid.NewGuid(), OntraportContactId = "c1", OntraportTransactionId = "t1", ContactName = "Bob Builder", Email = "bob@example.com", MobileNumber = "0",
            BusinessName = "BOB BUILDS", Abn = "22222222222", RenewalYears = 3, AmountPaid = 199, SyncedAt = T0, Status = OntraportSaleStatus.RenewalCompleted,
        };
        var log = new SearchLog { Id = Guid.NewGuid(), Abn = sale.Abn, SearchedAt = T0.AddHours(1), Success = true, ResultsCount = 1, InitiatedBy = SearchInitiator.System, Source = SearchSource.Local };
        var result = new SearchResult { Id = Guid.NewGuid(), SearchLogId = log.Id, SearchLog = log, BusinessName = "BOB BUILDS", AccountNumber = "9", RegistrationDate = "01/01/2019" };
        var run = new RenewalRequest { Id = Guid.NewGuid(), SearchResultId = result.Id, SearchResult = result, InitiatedAt = T0.AddHours(2), RenewalYears = 3, Amount = 199, Source = RenewalSource.Ontraport, Status = RenewalStatus.Completed, CompletedAt = T0.AddHours(2).AddMinutes(1), TransactionReference = "7E1" };
        sale.RenewalRequestId = run.Id;
        var order = new RenewalOrder { Id = Guid.NewGuid(), Source = RenewalOrderSource.Ontraport, SourceId = sale.Id, Abn = sale.Abn, BusinessName = sale.BusinessName, NameKey = "BOBBUILDS", Years = 3, AmountPaid = 199, PaidAt = T0, Stage = RenewalOrderStage.Renewed };
        run.RenewalOrderId = order.Id;
        db.AddRange(sale, log, result, run, order);
        db.SaveChanges();

        var fromSale = (await Journey(db, JourneyRecord.Sale, sale.Id))!.Events;
        Assert.Equal(
        [
            "Bob Builder paid $199.00 in Ontraport to renew BOB BUILDS for 3 years",
            "Renewtron looked up ABN 22222222222 at ASIC: 1 name: BOB BUILDS",
            "Renewtron started renewing BOB BUILDS at ASIC",
            "ASIC renewed BOB BUILDS",
        ], fromSale.Select(e => e.Text));
        Assert.Equal(fromSale, (await Journey(db, JourneyRecord.Order, order.Id))!.Events);
    }

    [Fact]
    public async Task A_retry_that_is_scheduled_reads_as_waiting()
    {
        var (db, ids) = Seed();
        var run = db.RenewalRequests.Single(r => r.Id == ids.FirstRun);
        run.HandledAt = null;
        run.NextRetryAt = new DateTime(2026, 11, 20, 20, 0, 0, DateTimeKind.Utc); // 21 Nov in Sydney
        db.SaveChanges();

        var waiting = Assert.Single((await Journey(db, JourneyRecord.Run, ids.FirstRun))!.Events, e => e.Text.StartsWith("Not renewed yet"));
        Assert.Equal(("Not renewed yet; Renewtron tries again 21 Nov", "waiting"), (waiting.Text, waiting.Meaning));
    }

    [Fact]
    public async Task Each_record_has_a_card_with_only_the_actions_allowed_now()
    {
        var (db, ids) = Seed();
        var records = (await Journey(db, JourneyRecord.Lead, ids.Lead))!.Records;

        // Every line opens to a card for its record.
        var events = (await Journey(db, JourneyRecord.Lead, ids.Lead))!.Events;
        Assert.All(events, e => Assert.Contains(records, c => c.Record == e.Record && c.Id == e.RecordId));

        // The declined run was handled: it can be put back, not retried. The BPAY run is paid: nothing to do.
        var first = Assert.Single(records, c => c.Id == ids.FirstRun);
        Assert.Equal(["put-back"], first.Actions);
        Assert.Contains(first.Facts, f => f.Label == "Status" && f.Value == "Handled outside Renewtron");
        var retry = Assert.Single(records, c => c.Id == ids.RetryRun);
        Assert.Empty(retry.Actions);
        Assert.Contains(retry.Facts, f => f.Label == "BPAY" && f.Value.StartsWith("Biller 17301, ref 8596799759492, $54.00: paid"));

        // Unhandled, retryable failure: Retry. Lodged but BPAY owed: Paid at the bank.
        var run = db.RenewalRequests.Single(r => r.Id == ids.FirstRun);
        run.HandledAt = null;
        run.FailedAtStep = "Search";
        db.RenewalRequests.Single(r => r.Id == ids.RetryRun).BpayPaidAt = null;
        db.SaveChanges();
        records = (await Journey(db, JourneyRecord.Lead, ids.Lead))!.Records;
        Assert.Equal(["retry"], Assert.Single(records, c => c.Id == ids.FirstRun).Actions);
        Assert.Equal(["bpay-paid"], Assert.Single(records, c => c.Id == ids.RetryRun).Actions);
    }

    [Fact]
    public async Task A_run_that_may_hold_asics_money_offers_no_retry()
    {
        var (db, ids) = Seed();
        db.RenewalRequests.Single(r => r.Id == ids.FirstRun).HandledAt = null; // failed at "Complete Payment Action"
        db.SaveChanges();

        var first = Assert.Single((await Journey(db, JourneyRecord.Run, ids.FirstRun))!.Records, c => c.Id == ids.FirstRun);
        Assert.DoesNotContain("retry", first.Actions);
    }

    [Fact]
    public async Task An_unknown_record_has_no_story()
    {
        var (db, _) = Seed();
        Assert.Null(await Journey(db, JourneyRecord.Lead, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("RenewedElsewhere", "renewed elsewhere")]
    [InlineData("CardDeclined", "card declined")]
    public void Reasons_read_as_words(string reason, string words) => Assert.Equal(words, JourneyService.Words(reason));
}
