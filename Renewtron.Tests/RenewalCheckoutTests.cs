using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Tests;

/// <summary>The wizard's money path: never charge twice, never record an intent against other names.</summary>
public class RenewalCheckoutTests
{
    private const decimal OneYear = 129m;

    private sealed class FakeStripe : IStripePaymentService
    {
        public int Confirms;
        public string? LastIdempotencyKey;
        public Dictionary<string, string>? LastMetadata;
        public StripeConfirmResult NextConfirm = new(true, false, "pi_1", null, new StripeCardInfo("visa", "4242", "12", "2030"), null);
        public StripeIntentState? Intent;

        public Task<StripeConfirmResult> ConfirmPaymentAsync(decimal amount, string customerEmail, string description,
            Dictionary<string, string> metadata, string paymentMethodId, string? idempotencyKey = null, bool testMode = false)
        {
            Confirms++;
            LastIdempotencyKey = idempotencyKey;
            LastMetadata = metadata;
            return Task.FromResult(NextConfirm);
        }

        public Task<StripeIntentState?> GetPaymentIntentAsync(string paymentIntentId, bool testMode = false) => Task.FromResult(Intent);
    }

    private sealed class FakeJobs : IBackgroundJobClient
    {
        public readonly List<Job> Created = [];
        public string Create(Job job, IState state) { Created.Add(job); return Created.Count.ToString(); }
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class FakeLeads : ILeadService
    {
        public Task<Lead> CreateLeadAsync(CreateLeadDto dto) => throw new NotSupportedException();
        public Task<Lead?> GetLeadAsync(Guid leadId) => throw new NotSupportedException();
        public Task<Lead?> GetLeadWithSearchResultsAsync(Guid leadId) => throw new NotSupportedException();
        public Task<Lead> UpdateLeadOutcomeAsync(Guid leadId, LeadOutcome outcome, string? message = null) => throw new NotSupportedException();
        public Task<Lead> LinkSearchLogAsync(Guid leadId, Guid searchLogId) => throw new NotSupportedException();
        public Task MarkConvertedAsync(Guid leadId) => Task.CompletedTask;
        public Task UpdateReminderOptInAsync(Guid leadId, bool optIn) => Task.CompletedTask;
    }

    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }

    private sealed record World(ApplicationDbContext Db, FakeStripe Stripe, FakeJobs Jobs, RenewalCheckout Checkout, Lead Lead, Guid[] ResultIds);

    private static World Seed(int names = 1, bool verified = true)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var lead = new Lead { Id = Guid.NewGuid(), Abn = "51824753556", FullName = "Jo Citizen", Email = "jo@example.com", MobileNumber = "0412345678", CreatedAt = DateTime.UtcNow };
        var log = new SearchLog
        {
            Id = Guid.NewGuid(), Abn = lead.Abn, SearchedAt = DateTime.UtcNow, Success = true,
            Source = SearchSource.Local, VerifiedAt = verified ? DateTime.UtcNow : null,
        };
        var results = Enumerable.Range(0, names).Select(i => new SearchResult
        {
            Id = Guid.NewGuid(), SearchLogId = log.Id, SearchLog = log, BusinessName = $"Name {i}", AccountNumber = $"10{i}", RegistrationDate = "01/01/2020",
        }).ToList();
        db.Leads.Add(lead);
        db.SearchLogs.Add(log);
        db.SearchResults.AddRange(results);
        db.SaveChanges();

        var stripe = new FakeStripe();
        var jobs = new FakeJobs();
        var checkout = new RenewalCheckout(db, stripe, jobs, new FakeLeads(),
            new Snapshot<PricingSettings>(new PricingSettings { OneYearFee = OneYear, ThreeYearFee = 299m }),
            new Snapshot<PortalSettings>(new PortalSettings()),
            new Snapshot<AsicSettings>(new AsicSettings()),
            new Snapshot<TestModeSettings>(new TestModeSettings()));
        return new World(db, stripe, jobs, checkout, lead, results.Select(r => r.Id).ToArray());
    }

    private static BatchRenewalRequest Pay(World w, int years = 1) => new(w.Lead.Id, w.ResultIds, years, "pm_1", "JO CITIZEN");

    private static StripeIntentState SucceededIntent(World w, decimal total, IEnumerable<Guid>? ids = null) => new(
        "pi_3ds", StripePaymentStatuses.Succeeded, (long)(total * 100),
        new Dictionary<string, string> { ["lead_id"] = w.Lead.Id.ToString(), ["ids_hash"] = RenewalCheckout.IdsHash(ids ?? w.ResultIds) },
        null);

    [Fact]
    public async Task Paying_records_each_name_and_queues_it()
    {
        var w = Seed(names: 2);

        var outcome = await w.Checkout.PayAsync(Pay(w));

        var paid = Assert.IsType<CheckoutOutcome.Paid>(outcome);
        Assert.Equal(2 * OneYear, paid.Total);
        Assert.Equal(2, paid.RenewalIds.Count);
        var renewals = await w.Db.RenewalRequests.Include(r => r.StripePayment).ToListAsync();
        Assert.All(renewals, r =>
        {
            Assert.Equal(RenewalStatus.Pending, r.Status);
            Assert.Equal(OneYear, r.Amount);
            Assert.Equal(StripePaymentStatuses.Succeeded, r.StripePayment!.PaymentStatus);
            Assert.Equal("4242", r.StripePayment.CardLast4);
        });
        Assert.Equal(2, w.Jobs.Created.Count(j => j.Type == typeof(IRenewalProcessingService)));
        Assert.Equal(RenewalCheckout.IdsHash(w.ResultIds), w.Stripe.LastMetadata!["ids_hash"]);
    }

    [Fact]
    public async Task A_second_payment_for_the_same_names_is_refused_before_charging()
    {
        var w = Seed();
        await w.Checkout.PayAsync(Pay(w));

        var again = await w.Checkout.PayAsync(Pay(w));

        var refused = Assert.IsType<CheckoutOutcome.Refused>(again);
        Assert.Equal(409, refused.StatusCode);
        Assert.Equal(1, w.Stripe.Confirms);
    }

    [Fact]
    public async Task Names_not_yet_confirmed_with_ASIC_are_not_payable()
    {
        var w = Seed(verified: false);

        var outcome = await w.Checkout.PayAsync(Pay(w));

        Assert.Equal("verifying", Assert.IsType<CheckoutOutcome.Refused>(outcome).Code);
        Assert.Equal(0, w.Stripe.Confirms);
    }

    [Fact]
    public async Task Three_d_secure_records_nothing_until_completed()
    {
        var w = Seed();
        w.Stripe.NextConfirm = new StripeConfirmResult(false, true, "pi_3ds", "secret_1", null, null);

        var outcome = await w.Checkout.PayAsync(Pay(w));

        Assert.Equal("secret_1", Assert.IsType<CheckoutOutcome.NeedsAction>(outcome).ClientSecret);
        Assert.Empty(w.Db.RenewalRequests);
        Assert.Empty(w.Jobs.Created);
    }

    [Fact]
    public async Task Completing_twice_returns_the_same_renewals()
    {
        var w = Seed();
        w.Stripe.Intent = SucceededIntent(w, OneYear);
        var request = new BatchRenewalCompleteRequest(w.Lead.Id, w.ResultIds, 1, "pi_3ds", null);

        var first = Assert.IsType<CheckoutOutcome.Paid>(await w.Checkout.CompleteAsync(request));
        var second = Assert.IsType<CheckoutOutcome.Paid>(await w.Checkout.CompleteAsync(request));

        Assert.Equal(first.RenewalIds, second.RenewalIds);
        Assert.Single(w.Db.RenewalRequests);
        Assert.Single(w.Db.StripePayments);
    }

    [Fact]
    public async Task A_paid_intent_cannot_be_moved_to_other_names()
    {
        var w = Seed();
        w.Stripe.Intent = SucceededIntent(w, OneYear, ids: [Guid.NewGuid()]);

        var outcome = await w.Checkout.CompleteAsync(new BatchRenewalCompleteRequest(w.Lead.Id, w.ResultIds, 1, "pi_3ds", null));

        Assert.Equal(422, Assert.IsType<CheckoutOutcome.Refused>(outcome).StatusCode);
        Assert.Empty(w.Db.RenewalRequests);
    }

    [Fact]
    public async Task An_intent_for_a_different_amount_is_refused()
    {
        var w = Seed();
        w.Stripe.Intent = SucceededIntent(w, OneYear - 1);

        var outcome = await w.Checkout.CompleteAsync(new BatchRenewalCompleteRequest(w.Lead.Id, w.ResultIds, 1, "pi_3ds", null));

        Assert.Contains("amount", Assert.IsType<CheckoutOutcome.Refused>(outcome).Error);
    }

    [Fact]
    public void The_idempotency_key_ignores_the_order_names_were_picked_in()
    {
        var lead = new Lead { Id = Guid.NewGuid(), Email = "jo@example.com" };
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        Assert.Equal(RenewalCheckout.IdempotencyKey(lead, 1, [a, b], 258m), RenewalCheckout.IdempotencyKey(lead, 1, [b, a], 258m));
        Assert.NotEqual(RenewalCheckout.IdempotencyKey(lead, 1, [a, b], 258m), RenewalCheckout.IdempotencyKey(lead, 1, [a, b], 259m));
    }
}
