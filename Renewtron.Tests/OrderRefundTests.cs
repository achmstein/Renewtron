using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Tests;

/// <summary>Refunding a paid renewal: only the payment a person picked, only once, and Renewtron never renews it after.</summary>
public class OrderRefundTests
{
    /// <summary>Ontraport as a script: invoices for one contact, and the refund call flipping one to refunded.</summary>
    private sealed class FakeOntraport : HttpMessageHandler
    {
        public readonly Dictionary<string, string> Status = new() { ["2778"] = "1", ["2700"] = "2" };
        public readonly List<string> Refunds = [];
        public bool RefundTakes = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Put && path.EndsWith("/transaction/refund"))
            {
                var body = await request.Content!.ReadAsStringAsync(ct);
                Refunds.Add(body);
                if (RefundTakes) foreach (var id in Status.Keys.ToList()) if (body.Contains(id)) Status[id] = "2";
                return Json("{\"code\":0,\"data\":\"\"}");
            }
            if (path.Contains("/objects?objectID=46"))
                return Json("{\"data\":[" + string.Join(",", Status.Select(s => $"{{\"id\":\"{s.Key}\",\"status\":\"{s.Value}\",\"type\":\"0\",\"total\":\"227.00\",\"invoice_date\":\"1776071465\"}}")) + "]}");
            if (path.Contains("/object?objectID=46&id="))
            {
                var id = path[(path.LastIndexOf('=') + 1)..];
                return Json($"{{\"data\":{{\"id\":\"{id}\",\"status\":\"{Status[id]}\"}}}}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    }

    private sealed class NoStripe : IStripePaymentService
    {
        public Task<StripeConfirmResult> ConfirmPaymentAsync(decimal amount, string customerEmail, string description, Dictionary<string, string> metadata, string paymentMethodId, string? idempotencyKey = null, bool testMode = false) => throw new NotSupportedException();
        public Task<StripeIntentState?> GetPaymentIntentAsync(string paymentIntentId, bool testMode = false) => throw new NotSupportedException();
        public Task<StripeRefundResult> RefundAsync(string paymentIntentId, string idempotencyKey, bool testMode = false) => throw new NotSupportedException();
        public Task<long?> RefundedCentsAsync(string paymentIntentId, bool testMode = false) => throw new NotSupportedException();
    }

    private sealed class NoRefresh : IRenewalOrderService
    {
        public Task<RenewalOrderRebuildReport> RebuildAsync(bool dryRun, CancellationToken ct) => throw new NotSupportedException();
        public Task RefreshAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RefreshQuietlyAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static (OrderRefundService Service, FakeOntraport Ontraport, ApplicationDbContext Db, Guid OrderId, Guid SaleId) Setup()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sale = new OntraportSale
        {
            Id = Guid.NewGuid(), OntraportContactId = "961858", OntraportTransactionId = "", ContactName = "Kane Gardner", Email = "k@example.com", MobileNumber = "0",
            BusinessName = "HAMMERHEAD ELECTRICAL", Abn = "72445221050", RenewalYears = 3, AmountPaid = 227, SyncedAt = DateTime.UtcNow, Status = OntraportSaleStatus.Synced,
        };
        var order = new RenewalOrder { Id = Guid.NewGuid(), Source = RenewalOrderSource.Ontraport, SourceId = sale.Id, Abn = sale.Abn, BusinessName = sale.BusinessName, NameKey = "HAMMERHEADELECTRICAL", Years = 3, AmountPaid = 227, PaidAt = DateTime.UtcNow, Stage = RenewalOrderStage.Due };
        db.AddRange(sale, order);
        db.SaveChanges();
        var fake = new FakeOntraport();
        var http = new HttpClient(fake) { BaseAddress = new Uri("https://api.ontraport.com/1/") };
        return (new OrderRefundService(http, new RefundHold(), db, new NoStripe(), new NoRefresh(), NullLogger<OrderRefundService>.Instance), fake, db, order.Id, sale.Id);
    }

    [Fact]
    public async Task Lists_the_contacts_payments_and_which_can_be_refunded()
    {
        var (service, _, _, orderId, _) = Setup();
        var options = (await service.OptionsAsync(orderId, CancellationToken.None))!;
        Assert.True(Assert.Single(options.Payments, p => p.Key == "ontraport:2778").Refundable);
        var done = Assert.Single(options.Payments, p => p.Key == "ontraport:2700");
        Assert.False(done.Refundable);
        Assert.Equal("Already refunded.", done.Why);
    }

    [Fact]
    public async Task Refunds_the_picked_invoice_closes_the_order_and_stops_the_sale()
    {
        var (service, ontraport, db, orderId, saleId) = Setup();
        var result = await service.RefundAsync(orderId, "ontraport:2778", "david@example.com", "Paid twice", CancellationToken.None);

        Assert.True(result.Ok, result.Message);
        Assert.Contains("\"ids\":[2778]", Assert.Single(ontraport.Refunds));
        var order = db.RenewalOrders.Single(o => o.Id == orderId);
        Assert.Equal((RenewalOrderReason.Refunded, "david@example.com", "Ontraport invoice 2778 refunded"), (order.Outcome, order.OutcomeBy, order.OutcomeReference));
        Assert.Contains("Paid twice", order.Evidence);
        Assert.NotNull(db.OntraportSales.Single(s => s.Id == saleId).HandledAt); // never renewed after a refund
    }

    [Fact]
    public async Task A_refunded_invoice_is_never_refunded_again()
    {
        var (service, ontraport, _, orderId, _) = Setup();
        Assert.True((await service.RefundAsync(orderId, "ontraport:2778", "d", null, CancellationToken.None)).Ok);
        var again = await service.RefundAsync(orderId, "ontraport:2778", "d", null, CancellationToken.None);
        Assert.False(again.Ok);
        Assert.Single(ontraport.Refunds);
    }

    [Fact]
    public async Task When_ontraport_doesnt_show_it_refunded_nothing_is_recorded_and_it_isnt_sent_again()
    {
        var (service, ontraport, db, orderId, _) = Setup();
        ontraport.RefundTakes = false;
        var first = await service.RefundAsync(orderId, "ontraport:2778", "d", null, CancellationToken.None);
        Assert.False(first.Ok);
        Assert.Contains("Check the invoice in Ontraport", first.Message);
        Assert.Null(db.RenewalOrders.Single(o => o.Id == orderId).Outcome);

        var second = await service.RefundAsync(orderId, "ontraport:2778", "d", null, CancellationToken.None);
        Assert.False(second.Ok);
        Assert.Single(ontraport.Refunds); // held back: the first may still go through
    }

    [Fact]
    public async Task A_payment_from_another_renewal_is_refused()
    {
        var (service, ontraport, _, orderId, _) = Setup();
        var result = await service.RefundAsync(orderId, "ontraport:9999", "d", null, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Empty(ontraport.Refunds);
    }
}
