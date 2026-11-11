using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>A payment on a paid renewal that a person may refund.</summary>
/// <param name="Key">What to send back to refund it: "ontraport:{invoice id}" or "stripe:{payment intent id}".</param>
/// <param name="Refundable">False when it's already refunded, not settled, or can't be read; <paramref name="Why"/> says which.</param>
public sealed record RefundablePayment(string Key, string Label, DateTime? PaidAt, decimal Amount, bool Refundable, string? Why);

public sealed record RefundOptions(IReadOnlyList<RefundablePayment> Payments, string? Note);

/// <summary>A refund done (Ok) or refused, in words.</summary>
public sealed record RefundOutcome(bool Ok, string Message, string? Reference = null);

/// <summary>
/// Refunds the customer for a paid renewal, through where they paid: Ontraport for an Ontraport sale
/// (refunds the invoice to their card), Stripe for a website card payment. Only ever when a person
/// asks, for the payment they picked. Afterwards the order is closed as Refunded with the reference,
/// and the sale or ASIC run behind it is stopped so Renewtron never renews a name it has refunded.
/// </summary>
public interface IOrderRefundService
{
    Task<RefundOptions?> OptionsAsync(Guid orderId, CancellationToken ct);
    Task<RefundOutcome> RefundAsync(Guid orderId, string paymentKey, string by, string? note, CancellationToken ct);
}

/// <summary>
/// App-wide memory of Ontraport refunds sent but not yet shown as refunded, so the same invoice is
/// never sent twice while Ontraport catches up. One per app (a singleton).
/// </summary>
public sealed class RefundHold
{
    private readonly ConcurrentDictionary<string, DateTime> _sent = new();
    public static readonly TimeSpan For = TimeSpan.FromHours(2);
    public DateTime? SentAt(string invoiceId) => _sent.TryGetValue(invoiceId, out var at) && DateTime.UtcNow - at < For ? at : null;
    public void Sent(string invoiceId) => _sent[invoiceId] = DateTime.UtcNow;
    public void Settled(string invoiceId) => _sent.TryRemove(invoiceId, out _);
}

public sealed class OrderRefundService(
    HttpClient ontraport,
    RefundHold hold,
    ApplicationDbContext db,
    IStripePaymentService stripe,
    IRenewalOrderService orders,
    ILogger<OrderRefundService> logger) : IOrderRefundService
{
    /// <summary>Ontraport's invoice statuses (object 46).</summary>
    private const string InvoiceClosed = "1", InvoiceRefunded = "2", InvoicePartlyRefunded = "3";

    // One refund at a time across the app; an invoice Ontraport accepted but hasn't shown as
    // refunded yet is held (RefundHold) so it's never sent twice.
    private static readonly SemaphoreSlim Lock = new(1);

    public async Task<RefundOptions?> OptionsAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.RenewalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return null;
        switch (order.Source)
        {
            case RenewalOrderSource.Ontraport:
                var contactId = await db.OntraportSales.AsNoTracking().Where(s => s.Id == order.SourceId).Select(s => s.OntraportContactId).FirstOrDefaultAsync(ct);
                if (string.IsNullOrEmpty(contactId)) return new([], "The Ontraport sale behind this renewal can't be found.");
                var invoices = await InvoicesAsync(contactId, ct);
                if (invoices is null) return new([], "Ontraport didn't answer. Try again in a minute.");
                return new(invoices.Select(Describe).ToList(),
                    invoices.Count == 0 ? "Ontraport has no payments for this contact." : "Every payment this Ontraport contact made is listed; pick the one to refund.");

            case RenewalOrderSource.Wizard:
                var run = await db.RenewalRequests.AsNoTracking().Include(r => r.StripePayment).FirstOrDefaultAsync(r => r.Id == order.SourceId, ct);
                if (run?.StripePayment is not { } pay) return new([], "No card payment is on record for this renewal.");
                var paid = pay.PaymentStatus == StripePaymentStatuses.Succeeded;
                var refunded = paid ? await stripe.RefundedCentsAsync(pay.PaymentIntentId, run.IsTest) : 0;
                var label = $"Card payment{(pay.CardLast4 is { Length: > 0 } l4 ? $" ({pay.CardBrand} ending {l4})" : "")} on the website";
                var why = !paid ? "The card payment didn't go through."
                    : refunded is null ? "Stripe didn't answer. Try again in a minute."
                    : refunded > 0 ? $"${refunded / 100m:0.00} has been refunded already." : null;
                return new([new($"stripe:{pay.PaymentIntentId}", label, pay.PaidAt, run.Amount, why is null, why)], null);

            default:
                return new([], "Staff recorded this payment; it was taken outside Renewtron, so refund it there and record it as refunded.");
        }
    }

    public async Task<RefundOutcome> RefundAsync(Guid orderId, string paymentKey, string by, string? note, CancellationToken ct)
    {
        await Lock.WaitAsync(ct);
        try
        {
            var options = await OptionsAsync(orderId, ct);
            if (options is null) return new(false, "This paid renewal no longer exists.");
            var payment = options.Payments.FirstOrDefault(p => p.Key == paymentKey);
            if (payment is null) return new(false, "That payment doesn't belong to this renewal. Refresh and pick again.");
            if (!payment.Refundable) return new(false, payment.Why ?? "That payment can't be refunded.");

            var order = await db.RenewalOrders.FirstAsync(o => o.Id == orderId, ct);
            if (order.Stage == RenewalOrderStage.Renewing)
                return new(false, "It's renewing at ASIC right now. Wait for the run to finish before refunding.");

            var (kind, id) = (paymentKey.Split(':', 2)[0], paymentKey.Split(':', 2)[1]);
            var done = kind switch
            {
                "ontraport" => await RefundInvoiceAsync(id, ct),
                "stripe" => await RefundCardAsync(order, id),
                _ => new RefundOutcome(false, "Unknown payment."),
            };
            if (!done.Ok) return done;

            await RecordAsync(order, payment, done.Reference!, by, note, ct);
            logger.LogWarning("Refunded {Amount} for renewal order {OrderId} ({Business}): {Reference}, by {By}",
                payment.Amount, order.Id, order.BusinessName, done.Reference, by);
            return done;
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task<RefundOutcome> RefundInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        if (hold.SentAt(invoiceId) is { } at)
            return new(false, $"A refund of Ontraport invoice {invoiceId} was already sent at {at:HH:mm} UTC. Check Ontraport before trying again.");

        var body = new StringContent(JsonSerializer.Serialize(new { ids = new[] { long.Parse(invoiceId) } }), Encoding.UTF8, "application/json");
        var response = await ontraport.PutAsync("transaction/refund", body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Ontraport refund of invoice {Invoice} returned {Status}: {Body}", invoiceId, (int)response.StatusCode, Clip(text));
            return new(false, $"Ontraport refused the refund ({(int)response.StatusCode}). Nothing was refunded.");
        }
        hold.Sent(invoiceId);

        // Believe it when Ontraport shows the invoice refunded, not before.
        for (var i = 0; i < 3; i++)
        {
            if (i > 0) await Task.Delay(TimeSpan.FromSeconds(2), ct);
            if (await InvoiceStatusAsync(invoiceId, ct) is InvoiceRefunded or InvoicePartlyRefunded)
            {
                hold.Settled(invoiceId); // Ontraport shows it refunded: that alone now stops a second one
                return new(true, $"Refunded Ontraport invoice {invoiceId} to the customer's card.", $"Ontraport invoice {invoiceId} refunded");
            }
        }
        logger.LogError("Ontraport took the refund of invoice {Invoice} but doesn't show it refunded: {Body}", invoiceId, Clip(text));
        return new(false, $"Ontraport took the refund request for invoice {invoiceId} but doesn't show it refunded yet. Check the invoice in Ontraport; if it's refunded there, record it as refunded here.");
    }

    private async Task<RefundOutcome> RefundCardAsync(RenewalOrder order, string paymentIntentId)
    {
        var test = await db.RenewalRequests.AsNoTracking().Where(r => r.Id == order.SourceId).Select(r => r.IsTest).FirstOrDefaultAsync();
        var result = await stripe.RefundAsync(paymentIntentId, $"refund-{order.Id}-{paymentIntentId}", test);
        return result.Success
            ? new(true, result.Status == "pending" ? "Refund sent; Stripe will settle it to the card in the next few days." : "Refunded to the customer's card.", $"Stripe refund {result.RefundId}")
            : new(false, $"Stripe didn't refund it: {result.ErrorMessage} Nothing was refunded.");
    }

    /// <summary>Closes the order as refunded and stops what would otherwise still renew it.</summary>
    private async Task RecordAsync(RenewalOrder order, RefundablePayment payment, string reference, string by, string? note, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var said = $"Refunded ${payment.Amount:0.00} ({payment.Label}).{(string.IsNullOrWhiteSpace(note) ? "" : $" {note.Trim()}")}";
        order.Outcome = RenewalOrderReason.Refunded;
        order.OutcomeAt = now;
        order.OutcomeBy = by;
        order.OutcomeReference = Clip(order.OutcomeReference is { Length: > 0 } earlier && earlier != reference ? $"{earlier}; {reference}" : reference, 200);
        order.Evidence = Clip(said, 1000);
        order.EvidenceSource = OrderEvidenceSource.Staff;
        order.EvidenceAt = now;

        if (order.Source == RenewalOrderSource.Ontraport && await db.OntraportSales.FirstOrDefaultAsync(s => s.Id == order.SourceId, ct) is { } sale && sale.HandledAt == null)
        {
            sale.HandledAt = now;
            sale.HandledNote = Clip($"Refunded by {by}: {reference}", 500);
        }
        foreach (var run in await db.RenewalRequests.Where(r => r.RenewalOrderId == order.Id || r.Id == order.SourceId).ToListAsync(ct))
        {
            if (run.Status is RenewalStatus.Completed or RenewalStatus.Processing) continue;
            run.NextRetryAt = null;
            run.HandledAt ??= now;
            run.HandledNote ??= Clip($"Refunded by {by}: {reference}", 500);
        }
        await db.SaveChangesAsync(ct);
        await orders.RefreshQuietlyAsync(ct);
    }

    private sealed record Invoice(string Id, string Status, string Type, decimal Total, DateTime? At);

    private static RefundablePayment Describe(Invoice i)
    {
        var why = i.Type != "0" ? "This is a refund receipt, not a payment."
            : i.Status switch
            {
                InvoiceClosed => null,
                InvoiceRefunded => "Already refunded.",
                InvoicePartlyRefunded => "Already partly refunded; finish it in Ontraport.",
                _ => "Not a settled payment (declined, pending or voided).",
            };
        return new($"ontraport:{i.Id}", $"Ontraport invoice {i.Id}", i.At, i.Total, why is null, why);
    }

    /// <summary>The contact's invoices, newest first; null when Ontraport didn't answer.</summary>
    private async Task<List<Invoice>?> InvoicesAsync(string contactId, CancellationToken ct)
    {
        var condition = Uri.EscapeDataString($"[{{\"field\":{{\"field\":\"contact_id\"}},\"op\":\"=\",\"value\":{{\"value\":\"{contactId}\"}}}}]");
        var data = await GetDataAsync($"objects?objectID=46&condition={condition}&listFields=id,status,type,total,invoice_date&sort=id&sortDir=desc&range=50", ct);
        if (data is not { ValueKind: JsonValueKind.Array } rows) return null;
        return rows.EnumerateArray().Select(Read).ToList();
    }

    private async Task<string?> InvoiceStatusAsync(string invoiceId, CancellationToken ct) =>
        await GetDataAsync($"object?objectID=46&id={invoiceId}", ct) is { ValueKind: JsonValueKind.Object } row ? Text(row, "status") : null;

    private async Task<JsonElement?> GetDataAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await ontraport.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("data", out var data) ? data.Clone() : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ontraport read {Url} failed", url);
            return null;
        }
    }

    private static Invoice Read(JsonElement e) => new(
        Text(e, "id") ?? "",
        Text(e, "status") ?? "",
        Text(e, "type") ?? "0",
        decimal.TryParse(Text(e, "total"), out var t) ? t : 0,
        long.TryParse(Text(e, "invoice_date"), out var unix) && unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : null);

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

    private static string Clip(string s, int max = 500) => s.Length > max ? s[..max] : s;
}
