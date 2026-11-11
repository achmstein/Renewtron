using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Tests;

/// <summary>Paying early: when a renewal ASIC isn't taking yet is looked at again.</summary>
public class EarlyRenewalTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    [Theory]
    [InlineData("15/03/2020", "2027-03-15")]   // anniversary later this cycle
    [InlineData("07/11/2019", "2026-11-07")]   // due today
    [InlineData("20/11/2024", "2026-11-20")]   // inside the window
    [InlineData("01/01/2026", "2027-01-01")]   // registered this year
    [InlineData("29/02/2024", "2027-02-28")]   // leap day
    public void Due_date_is_the_next_registration_anniversary(string registered, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), EarlyRenewalService.EstimatedDueDate(Today, registered));

    [Fact]
    public void Unreadable_registration_date_has_no_due_date() =>
        Assert.Null(EarlyRenewalService.EstimatedDueDate(Today, "soon"));

    [Fact]
    public void Before_the_window_checks_on_the_day_it_opens() =>
        // Due 15/03/2027: ASIC opens it 29 days before.
        Assert.Equal(new DateOnly(2027, 2, 14), EarlyRenewalService.NextCheck(Today, "15/03/2020"));

    [Fact]
    public void Inside_the_window_checks_again_tomorrow() =>
        Assert.Equal(Today.AddDays(1), EarlyRenewalService.NextCheck(Today, "20/11/2024"));

    [Fact]
    public void No_registration_date_checks_in_a_week() =>
        Assert.Equal(Today.AddDays(7), EarlyRenewalService.NextCheck(Today, null));

    [Fact]
    public void Waiting_renewal_is_marked_and_recognised()
    {
        var r = new RenewalRequest();
        EarlyRenewalService.MarkWaiting(r, new DateOnly(2027, 2, 14));
        Assert.True(EarlyRenewalService.IsWaiting(r.Status, r.ErrorCategory, r.NextRetryAt));
        Assert.Equal(new DateTime(2027, 2, 14, 0, 0, 0, DateTimeKind.Utc), r.NextRetryAt);
        Assert.Equal(RenewalErrorCategories.NotDueYet, r.ErrorCategory);
    }

    [Fact]
    public void Customer_is_never_told_a_waiting_renewal_is_not_due()
    {
        var m = RenewalErrorClassifier.CustomerMessage(RenewalStatus.Failed, RenewalErrorCategories.NotDueYet, DateTime.UtcNow);
        Assert.DoesNotContain("due", m!, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The morning check (EarlyRenewalService.CheckWaitingAsync) against an in-memory database and a fake ASIC.</summary>
public class EarlyRenewalCheckTests
{
    private sealed class FakeAsic(Asic.Integrations.Models.BusinessNamesResult answer) : IAsicSearchCoordinator
    {
        public int Searches;
        public Task<Asic.Integrations.Models.BusinessNamesResult> SearchAsync(string abn) { Searches++; return Task.FromResult(answer); }
        public void Prefetch(string abn) { }
        public bool TryGetRecent(string abn, out Asic.Integrations.Models.BusinessNamesResult result) { result = answer; return false; }
    }

    private sealed class FakeJobs : Hangfire.IBackgroundJobClient
    {
        public List<string> Created = [];
        public string Create(Hangfire.Common.Job job, Hangfire.States.IState state)
        {
            Created.Add($"{job.Type.Name}.{job.Method.Name}({string.Join(",", job.Args)})");
            return Guid.NewGuid().ToString();
        }
        public bool ChangeState(string jobId, Hangfire.States.IState state, string expectedState) => true;
    }

    private static (ApplicationDbContext Db, RenewalRequest Renewal) Seed(DateTime checkAt, string name = "Beautique Hair Lounge & Beauty Bar")
    {
        var db = new ApplicationDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var log = new SearchLog { Id = Guid.NewGuid(), Abn = "49642383462", SearchedAt = DateTime.UtcNow, Success = true, Source = SearchSource.Local, VerifiedAt = DateTime.UtcNow };
        var sr = new SearchResult { Id = Guid.NewGuid(), SearchLogId = log.Id, BusinessName = name, AccountNumber = "", RegistrationDate = "08/10/2020", PayEarly = true, SearchLog = log };
        var renewal = new RenewalRequest { Id = Guid.NewGuid(), SearchResultId = sr.Id, SearchResult = sr, Source = RenewalSource.Renewtron, PaymentType = PaymentType.Stripe, InitiatedAt = DateTime.UtcNow, RenewalYears = 1 };
        EarlyRenewalService.MarkWaiting(renewal, DateOnly.FromDateTime(checkAt));
        renewal.NextRetryAt = checkAt;
        db.SearchLogs.Add(log); db.SearchResults.Add(sr); db.RenewalRequests.Add(renewal);
        db.SaveChanges();
        return (db, renewal);
    }

    private static Task<EarlyRenewalReport> Check(ApplicationDbContext db, FakeAsic asic, FakeJobs jobs,
        Asic.Integrations.Models.AsicRegistrySearchResult? register = null) =>
        new EarlyRenewalService(db, asic, jobs, AsicDueDateTests.Service(db, register ?? Asic.Integrations.Models.AsicRegistrySearchResult.Fail("offline")),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EarlyRenewalService>.Instance).CheckWaitingAsync();

    [Fact]
    public async Task Still_not_due_waits_for_the_window_of_asics_real_due_date()
    {
        var (db, renewal) = Seed(DateTime.UtcNow.AddMinutes(-1));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time")));
        var realDue = today.AddDays(90); // not the registration anniversary (08/10)
        var asic = new FakeAsic(new() { Success = false, ErrorMessage = "This business name is not due for renewal yet. ASIC will send a renewal notice when it becomes due." });
        var register = AsicDueDateTests.Register(("BEAUTIQUE HAIR LOUNGE & BEAUTY BAR", realDue.ToString("dd/MM/yyyy")));

        await Check(db, asic, new FakeJobs(), register);

        Assert.Equal(EarlyRenewalService.CheckAt(realDue.AddDays(-RenewalOrderProjection.WindowDays)), renewal.NextRetryAt);
    }

    [Fact]
    public async Task Once_asic_lists_the_name_it_gets_the_account_number_and_is_lodged()
    {
        var (db, renewal) = Seed(DateTime.UtcNow.AddMinutes(-1));
        var asic = new FakeAsic(new() { Success = true, BusinessNames = [new() { Name = "BEAUTIQUE HAIR LOUNGE &amp; BEAUTY BAR", AccountNumber = "123456789", RegistrationDate = "08/10/2020" }] });
        var jobs = new FakeJobs();

        var report = await Check(db, asic, jobs);

        Assert.Equal(1, report.Started);
        Assert.Equal(RenewalStatus.Pending, renewal.Status);
        Assert.Null(renewal.ErrorCategory);
        Assert.Null(renewal.NextRetryAt);
        Assert.Equal("123456789", renewal.SearchResult.AccountNumber);
        Assert.Contains(jobs.Created, j => j.StartsWith("IRenewalProcessingService.ProcessRenewalAsync") && j.Contains(renewal.Id.ToString()));
    }

    [Fact]
    public async Task Still_not_due_waits_for_the_window_again()
    {
        var (db, renewal) = Seed(DateTime.UtcNow.AddMinutes(-1));
        var asic = new FakeAsic(new() { Success = false, ErrorMessage = "This business name is not due for renewal yet. ASIC will send a renewal notice when it becomes due." });
        var jobs = new FakeJobs();

        var report = await Check(db, asic, jobs);

        Assert.Equal((0, 1), (report.Started, report.StillWaiting));
        Assert.True(EarlyRenewalService.IsWaiting(renewal.Status, renewal.ErrorCategory, renewal.NextRetryAt));
        Assert.True(renewal.NextRetryAt > DateTime.UtcNow);
        Assert.DoesNotContain(jobs.Created, j => j.StartsWith("IRenewalProcessingService"));
    }

    [Fact]
    public async Task Other_names_due_but_not_this_one_keeps_waiting()
    {
        var (db, renewal) = Seed(DateTime.UtcNow.AddMinutes(-1));
        var asic = new FakeAsic(new() { Success = true, BusinessNames = [new() { Name = "SOMETHING ELSE", AccountNumber = "1", RegistrationDate = "01/01/2020" }] });

        await Check(db, asic, new FakeJobs());

        Assert.True(EarlyRenewalService.IsWaiting(renewal.Status, renewal.ErrorCategory, renewal.NextRetryAt));
    }

    [Fact]
    public async Task No_names_under_the_abn_goes_to_a_person()
    {
        var (db, renewal) = Seed(DateTime.UtcNow.AddMinutes(-1));
        var asic = new FakeAsic(new() { Success = false, ErrorMessage = "No business names found for this ABN" });

        var report = await Check(db, asic, new FakeJobs());

        Assert.Equal(1, report.NeedPerson);
        Assert.Equal(RenewalErrorCategories.Terminal, renewal.ErrorCategory);
        Assert.Null(renewal.NextRetryAt);
    }

    [Fact]
    public async Task A_renewal_whose_check_date_hasnt_come_is_left_alone()
    {
        var (db, renewal) = Seed(DateTime.UtcNow.AddDays(30));
        var asic = new FakeAsic(new() { Success = true, BusinessNames = [] });

        var report = await Check(db, asic, new FakeJobs());

        Assert.Equal(0, report.Checked);
        Assert.Equal(0, asic.Searches);
    }
}
