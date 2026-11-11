using Asic.Integrations.Renewal;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Tests;

/// <summary>The wizard's details and check steps: where the names come from, and what's recorded.</summary>
public class LeadWizardTests
{
    private const string Abn = "51824753556";

    private sealed class FakeAsic : IAsicSearchCoordinator
    {
        public BusinessNamesResult? Recent;
        public BusinessNamesResult Live = BusinessNamesResult.Failed("Search failed: not set up");
        public int Searches, Prefetches;
        public Task<BusinessNamesResult> SearchAsync(string abn) { Searches++; return Task.FromResult(Live); }
        public void Prefetch(string abn) => Prefetches++;
        public bool TryGetRecent(string abn, out BusinessNamesResult result) { result = Recent!; return Recent is not null; }
    }

    private sealed class FakeLocal : IBusinessNameFallbackService
    {
        public BusinessNamesResult Result = BusinessNamesResult.Succeeded([]);
        public Task<BusinessNamesResult> SearchByAbnAsync(string abn) => Task.FromResult(Result);
    }

    private sealed class FakeLeadEmail : ILeadEmailService
    {
        public readonly List<string> Sent = [];
        public Task SendLeadCapturedEmailAsync(Lead lead) { Sent.Add("captured"); return Task.CompletedTask; }
        public Task SendNotDueForRenewalEmailAsync(Lead lead) { Sent.Add("not-due"); return Task.CompletedTask; }
        public Task SendNoBusinessNamesEmailAsync(Lead lead) { Sent.Add("no-names"); return Task.CompletedTask; }
        public Task SendRenewalInProgressEmailAsync(Lead lead) { Sent.Add("in-progress"); return Task.CompletedTask; }
        public Task SendWinBackEmailAsync(Lead lead, string? firstBusinessName = null, Guid? outboundMessageId = null) => Task.CompletedTask;
        public Task<bool> SendPaymentNotFinishedEmailAsync(Lead lead, string? firstBusinessName, string renewLink) => Task.FromResult(true);
    }

    private sealed class FakeJobs : IBackgroundJobClient
    {
        public readonly List<Job> Created = [];
        public string Create(Job job, IState state) { Created.Add(job); return Created.Count.ToString(); }
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }

    private sealed record World(ApplicationDbContext Db, LeadWizard Wizard, FakeAsic Asic, FakeLocal Local, FakeLeadEmail Email, FakeJobs Jobs);

    private static World Build(AsicSettings? asicSettings = null)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var asic = new FakeAsic();
        var local = new FakeLocal();
        var email = new FakeLeadEmail();
        var jobs = new FakeJobs();
        var wizard = new LeadWizard(db, new LeadService(db), email, asic, local,
            new Snapshot<AsicSettings>(asicSettings ?? new AsicSettings { AllowPayEarly = true }),
            new Snapshot<TestModeSettings>(new TestModeSettings { TestEmails = "test@example.com" }),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            jobs, NullLogger<LeadWizard>.Instance);
        return new World(db, wizard, asic, local, email, jobs);
    }

    private static BusinessNamesResult Names(params string[] names) =>
        BusinessNamesResult.Succeeded(names.Select(n => new BusinessName { Name = n, AccountNumber = "", RegistrationDate = "01/01/2020" }).ToList());

    private static CreateLeadRequest Details(string email = "jo@example.com") =>
        new(Abn, "Jo Citizen", email, "0412345678", new DateOnly(1980, 1, 1), " 123 456 782 ", null, "  ONTRAPORT-email-broadcast  ", null, null);

    private static async Task<Guid> Capture(World w, string email = "jo@example.com") =>
        (await w.Wizard.CaptureAsync(Details(email), new RequestOrigin("1.2.3.4", "test", "trace"))).LeadId!.Value;

    [Fact]
    public async Task Capture_refuses_a_bad_ABN_before_storing_anything()
    {
        var w = Build();

        var result = await w.Wizard.CaptureAsync(Details() with { Abn = "123" }, new RequestOrigin(null, null, null));

        Assert.Equal("ABN must be 11 digits.", result.Error);
        Assert.Empty(w.Db.Leads);
    }

    [Fact]
    public async Task Capture_tidies_the_details_and_pushes_the_contact()
    {
        var w = Build();

        var id = await Capture(w);

        var lead = await w.Db.Leads.SingleAsync(l => l.Id == id);
        Assert.Equal("123456782", lead.Tfn);
        Assert.Equal("ONTRAPORT-email-broadcast", lead.Source);
        Assert.Equal("1.2.3.4", lead.IpAddress);
        Assert.False(lead.IsTest);
        Assert.Contains("captured", w.Email.Sent);
        Assert.Contains(w.Jobs.Created, j => j.Type == typeof(IOntraportContactPushService));
    }

    [Fact]
    public async Task A_finished_ASIC_search_is_used_as_is_and_counts_as_verified()
    {
        var w = Build();
        w.Asic.Recent = Names("Harbour Light Plumbing");
        var id = await Capture(w);

        var result = await w.Wizard.CheckNamesAsync(id);

        Assert.Equal(LeadOutcome.RenewalAvailable, result!.Outcome);
        Assert.True(result.Verified);
        Assert.Equal("Harbour Light Plumbing", Assert.Single(result.BusinessNames).BusinessName);
        Assert.Equal(SearchSource.Asic, (await w.Db.SearchLogs.SingleAsync()).Source);
        Assert.Equal(0, w.Asic.Searches);
    }

    [Fact]
    public async Task Names_from_our_copy_wait_for_ASIC_to_confirm_them()
    {
        var w = Build(new AsicSettings { ForceFallback = true });
        w.Local.Result = Names("Harbour Light Plumbing", "Second Name");
        var id = await Capture(w);

        var result = await w.Wizard.CheckNamesAsync(id);

        Assert.Equal(2, result!.BusinessNames.Count);
        Assert.Equal(SearchSource.Local, (await w.Db.SearchLogs.SingleAsync()).Source);
        Assert.Equal(0, w.Asic.Searches);
    }

    [Fact]
    public async Task Not_due_names_are_offered_to_pay_early()
    {
        var w = Build();
        w.Asic.Live = BusinessNamesResult.Failed("This business name is not due for renewal yet");
        var id = await Capture(w);
        // Our copy has no names at first (so ASIC is asked), then has them for the pay-early offer.
        var calls = 0;
        var result = await CheckWithLocalAfterFirstCall(w, id, () => calls++ == 0 ? Names() : Names("Harbour Light Plumbing"));

        Assert.Equal(LeadOutcome.RenewalAvailable, result!.Outcome);
        Assert.True(result.Verified);
        var saved = await w.Db.SearchResults.SingleAsync();
        Assert.True(saved.PayEarly);
        Assert.DoesNotContain("not-due", w.Email.Sent);
    }

    [Fact]
    public async Task No_names_records_the_outcome_and_sends_its_email()
    {
        var w = Build();
        w.Asic.Live = BusinessNamesResult.Failed("A renewal for this ABN is already in progress");
        var id = await Capture(w);

        var result = await w.Wizard.CheckNamesAsync(id);

        Assert.Equal(LeadOutcome.RenewalInProgress, result!.Outcome);
        Assert.Empty(result.BusinessNames);
        Assert.Equal(LeadOutcome.RenewalInProgress, (await w.Db.Leads.SingleAsync()).Outcome);
        Assert.False((await w.Db.SearchLogs.SingleAsync()).Success);
        Assert.Contains("in-progress", w.Email.Sent);
    }

    [Fact]
    public async Task A_test_customer_gets_our_copy_and_is_never_pushed_to_Ontraport()
    {
        var w = Build(new AsicSettings { AllowPayEarly = false });
        w.Local.Result = Names("Harbour Light Plumbing");
        var id = await Capture(w, "test@example.com");
        w.Jobs.Created.Clear();

        var result = await w.Wizard.CheckNamesAsync(id);

        Assert.True(result!.Verified);
        Assert.Empty(w.Jobs.Created);
        Assert.Equal(0, w.Asic.Searches);
    }

    [Fact]
    public async Task An_unknown_lead_is_null()
    {
        Assert.Null(await Build().Wizard.CheckNamesAsync(Guid.NewGuid()));
    }

    /// <summary>Runs the check with the local register answering differently per call.</summary>
    private static async Task<LeadCheckResult?> CheckWithLocalAfterFirstCall(World w, Guid id, Func<BusinessNamesResult> next)
    {
        var sequenced = new SequencedLocal(next);
        var wizard = new LeadWizard(w.Db, new LeadService(w.Db), w.Email, w.Asic, sequenced,
            new Snapshot<AsicSettings>(new AsicSettings { AllowPayEarly = true }),
            new Snapshot<TestModeSettings>(new TestModeSettings()),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            w.Jobs, NullLogger<LeadWizard>.Instance);
        return await wizard.CheckNamesAsync(id);
    }

    private sealed class SequencedLocal(Func<BusinessNamesResult> next) : IBusinessNameFallbackService
    {
        public Task<BusinessNamesResult> SearchByAbnAsync(string abn) => Task.FromResult(next());
    }
}
