using System.Net;
using System.Text;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Tests;

/// <summary>
/// Bringing one Ontraport contact's sale in on the spot (the welcome page after an Ontraport
/// checkout), and reading when the contact paid.
/// </summary>
public class OntraportWelcomeTests
{
    private const string ContactId = "1158509";

    /// <summary>Answers each Ontraport request by its path: the contact, or the contact's invoices.</summary>
    private sealed class FakeOntraport(string contactJson, string invoicesJson = "{\"data\":[]}") : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Asked.Add(url);
            var body = url.Contains("objects?objectID=46") ? invoicesJson : contactJson;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeJobs : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => "1";
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }

    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static (OntraportSalesService Service, ApplicationDbContext Db, FakeOntraport Http) Build(string contactJson, string? invoicesJson = null)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var http = invoicesJson is null ? new FakeOntraport(contactJson) : new FakeOntraport(contactJson, invoicesJson);
        var service = new OntraportSalesService(
            new HttpClient(http) { BaseAddress = new Uri("https://api.ontraport.com/1/") },
            db,
            new FakeJobs(),
            asicClient: null!, // not used by these paths
            new Snapshot<PricingSettings>(new PricingSettings { OneYearFee = 99 }),
            new Monitor<AsicKeyRequestSettings>(new AsicKeyRequestSettings { Enabled = false }),
            new Monitor<AsicKeyInboxSettings>(new AsicKeyInboxSettings()),
            NullLogger<OntraportSalesService>.Instance,
            new ServiceCollection().BuildServiceProvider());
        return (service, db, http);
    }

    private static string Contact(string paymentReceived) =>
        $$$"""{"data":{"id":"{{{ContactId}}}","firstname":"Jane","lastname":"Citizen","email":"jane@example.com","sms_number":"0412345678","spent":"233","refund":"0","f5062":"Jane's Plumbing","f5063":"51824753556","f5194":"{{{paymentReceived}}}","f5193":"2033","f5135":"0"}}""";

    [Fact]
    public async Task A_paid_contact_becomes_one_sale_even_when_asked_again()
    {
        var (service, db, _) = Build(Contact("yes"));

        var first = await service.SyncContactAsync(ContactId);
        var second = await service.SyncContactAsync(ContactId);

        Assert.NotNull(first);
        Assert.Equal(first!.Id, second!.Id);
        var sale = Assert.Single(await db.OntraportSales.ToListAsync());
        Assert.Equal("Jane's Plumbing", sale.BusinessName);
        Assert.Equal("jane@example.com", sale.Email);
        Assert.Null(sale.PortalLinkIssuedAt);
    }

    [Fact]
    public async Task A_contact_Ontraport_hasnt_marked_paid_isnt_brought_in()
    {
        var (service, db, _) = Build(Contact(""));

        Assert.Null(await service.SyncContactAsync(ContactId));
        Assert.Empty(await db.OntraportSales.ToListAsync());
    }

    [Fact]
    public async Task Paid_at_is_the_newest_paid_invoice_and_asks_only_for_this_contact()
    {
        var (service, _, http) = Build(Contact("yes"), """{"data":[{"id":"7927","date":"1795001284"}]}""");

        var paidAt = await service.LastPaidAtAsync(ContactId);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795001284).UtcDateTime, paidAt);
        var asked = Uri.UnescapeDataString(Assert.Single(http.Asked));
        Assert.Contains("\"contact_id\"", asked);
        Assert.Contains($"\"value\":\"{ContactId}\"", asked);
        Assert.Contains("\"status\"},\"op\":\"=\",\"value\":{\"value\":\"1\"}", asked);
    }

    [Fact]
    public async Task No_paid_invoice_means_no_paid_at()
    {
        var (service, _, _) = Build(Contact("yes"));
        Assert.Null(await service.LastPaidAtAsync(ContactId));
    }
}
