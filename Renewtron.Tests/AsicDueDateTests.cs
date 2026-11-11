using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Services.AsicRegistry;
using Renewtron.Settings;
using Asic.Integrations.Registry;

namespace Renewtron.Tests;

/// <summary>Real due dates from ASIC's register search, in place of estimates.</summary>
public class AsicDueDateTests
{
    private const string Abn = "49642383462";

    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    public sealed class FakeRegistry(AsicRegistrySearchResult answer) : IAsicRegistryClientFactory, IAsicRegistryClient
    {
        public int Lookups;
        public IAsicRegistryClient Create() => this;
        public Task<AsicRegistrySearchResult> SearchByAbnAsync(string abn, CancellationToken ct) { Lookups++; return Task.FromResult(answer); }
        public Task<AsicRegistrySearchResult> SearchByAbnAndNameAsync(string abn, string name, CancellationToken ct) => SearchByAbnAsync(abn, ct);
    }

    public static AsicRegistrySearchResult Register(params (string Name, string? RenewalDate)[] names) =>
        AsicRegistrySearchResult.Ok(names.Select(n => new AsicRegistryBusinessName(n.Name, "Registered", "01/01/2015", n.RenewalDate, null, null, null, null, [])).ToList());

    public static AsicDueDateService Service(ApplicationDbContext db, AsicRegistrySearchResult answer, AsicRegistrySettings? settings = null) =>
        Service(db, new FakeRegistry(answer), settings);

    public static AsicDueDateService Service(ApplicationDbContext db, FakeRegistry registry, AsicRegistrySettings? settings = null) =>
        new(db, registry, new Monitor<AsicRegistrySettings>(settings ?? new()), NullLogger<AsicDueDateService>.Instance);

    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ClientBusinessName Name(ApplicationDbContext db, string name, DateOnly? due = null, RenewalDueDateSource? source = null, DateTime? checkedAt = null, string abn = Abn)
    {
        var row = new ClientBusinessName
        {
            Id = Guid.NewGuid(), Abn = abn, Name = name, RenewalDueDate = due, DueDateSource = source,
            DueDateCheckedAt = checkedAt, Status = ClientBusinessNameStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.ClientBusinessNames.Add(row);
        db.SaveChanges();
        return row;
    }

    [Theory]
    [InlineData("15/03/2027", 2027, 3, 15)]
    [InlineData(" 5/3/2027 ", 2027, 3, 5)]
    [InlineData("2027-03-15", 2027, 3, 15)]
    [InlineData("15 Mar 2027", 2027, 3, 15)]
    public void Reads_asics_date_text_day_first(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), AsicDates.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("n/a")]
    public void Unreadable_dates_are_null(string? text) => Assert.Null(AsicDates.Parse(text));

    [Fact]
    public void Asics_date_replaces_an_estimate_even_when_earlier() =>
        Assert.True(AsicDueDateService.ShouldTake(new DateOnly(2027, 6, 1), RenewalDueDateSource.Estimated, new DateOnly(2027, 3, 1)));

    [Fact]
    public void Our_own_later_renewal_date_stands_while_asic_catches_up() =>
        Assert.False(AsicDueDateService.ShouldTake(new DateOnly(2028, 3, 1), RenewalDueDateSource.Renewal, new DateOnly(2027, 3, 1)));

    [Fact]
    public void A_known_due_date_drives_the_next_check_over_the_anniversary()
    {
        var today = new DateOnly(2026, 11, 7);
        // Registered 15/03 (anniversary estimate 15/03/2027) but ASIC says due 01/09/2027 (a 3-year term, say).
        Assert.Equal(new DateOnly(2027, 8, 3), EarlyRenewalService.NextCheck(today, "15/03/2020", new DateOnly(2027, 9, 1)));
    }

    [Fact]
    public void A_known_due_date_in_the_past_is_ignored() =>
        Assert.Equal(new DateOnly(2027, 2, 14),
            EarlyRenewalService.NextCheck(new DateOnly(2026, 11, 7), "15/03/2020", new DateOnly(2026, 1, 1)));

    [Fact]
    public async Task Refresh_records_asics_date_on_matching_names()
    {
        var db = Db();
        var estimated = Name(db, "Beautique Hair Lounge", new DateOnly(2027, 6, 1), RenewalDueDateSource.Estimated);
        var unknown = Name(db, "Second Name");
        var service = Service(db, Register(("BEAUTIQUE HAIR LOUNGE", "15/03/2027"), ("SECOND NAME", "01/09/2028")));

        var report = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal((1, 2, 0), (report.Looked, report.Updated, report.Failed)); // one ABN, one lookup
        Assert.Equal((new DateOnly(2027, 3, 15), RenewalDueDateSource.AsicRegister), (estimated.RenewalDueDate, estimated.DueDateSource));
        Assert.Equal((new DateOnly(2028, 9, 1), RenewalDueDateSource.AsicRegister), (unknown.RenewalDueDate, unknown.DueDateSource));
        Assert.NotNull(unknown.DueDateCheckedAt);
    }

    [Fact]
    public async Task Refresh_skips_names_read_recently_and_far_from_due()
    {
        var db = Db();
        Name(db, "Fresh", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2), RenewalDueDateSource.AsicRegister, DateTime.UtcNow.AddDays(-40));
        Name(db, "Just read", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), RenewalDueDateSource.AsicRegister, DateTime.UtcNow.AddDays(-1), abn: "51824753556");
        var registry = new FakeRegistry(Register());

        var report = await Service(db, registry).RefreshAsync(CancellationToken.None);

        Assert.Equal(0, registry.Lookups);
        Assert.Equal(0, report.Looked);
    }

    [Fact]
    public async Task Refresh_stops_when_asic_keeps_failing()
    {
        var db = Db();
        foreach (var abn in new[] { "11111111111", "22222222222", "33333333333", "44444444444" }) Name(db, "N" + abn, abn: abn);
        var registry = new FakeRegistry(AsicRegistrySearchResult.Fail("down"));

        var report = await Service(db, registry, new AsicRegistrySettings { ConsecutiveFailureLimit = 2 }).RefreshAsync(CancellationToken.None);

        Assert.True(report.Stopped);
        Assert.Equal(2, registry.Lookups);
    }

    [Fact]
    public async Task Switched_off_asks_nobody()
    {
        var db = Db();
        Name(db, "Anything");
        var registry = new FakeRegistry(Register());

        await Service(db, registry, new AsicRegistrySettings { Enabled = false }).RefreshAsync(CancellationToken.None);

        Assert.Equal(0, registry.Lookups);
    }
}
