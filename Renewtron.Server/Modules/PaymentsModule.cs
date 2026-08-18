using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

/// <summary>
/// Every Stripe payment attempt in one place: succeeded charges (StripePayments, one row
/// per PaymentIntent — a batch of names shares one charge) and failed attempts (the
/// payment_failed funnel events, whose Detail carries the exact Stripe error the customer saw).
/// Failed attempts never create renewal rows, so before this page they were invisible
/// everywhere except the funnel totals.
/// </summary>
public sealed class PaymentsModule : ICarterModule
{
    private sealed record PaymentRow(
        string Kind,               // "succeeded" | "failed"
        DateTime When,
        string? PaymentIntentId,
        decimal? Amount,
        string? Abn,
        List<string> BusinessNames,
        Guid? LeadId,
        string? CustomerName,
        string? Email,
        string? CardBrand,
        string? CardLast4,
        string? CardholderName,
        string? Error,
        List<PaymentRenewalRef> Renewals);

    private sealed record PaymentRenewalRef(Guid Id, string Status);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/payments", async (
            ApplicationDbContext db,
            string? status = null,
            string? search = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            int page = 1,
            int pageSize = 20) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var rows = new List<PaymentRow>();

            if (!string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                var payments = await db.StripePayments.AsNoTracking()
                    .Include(p => p.RenewalRequest).ThenInclude(r => r.SearchResult).ThenInclude(s => s.SearchLog)
                    .Include(p => p.RenewalRequest).ThenInclude(r => r.Lead)
                    .Where(p => p.PaymentStatus == "succeeded")
                    .ToListAsync();

                rows.AddRange(payments
                    .GroupBy(p => p.PaymentIntentId)
                    .Select(g =>
                    {
                        var first = g.OrderBy(p => p.PaidAt).First();
                        var lead = g.Select(p => p.RenewalRequest.Lead).FirstOrDefault(l => l is not null);
                        var withCard = g.FirstOrDefault(p => p.CardLast4 != null) ?? first;
                        return new PaymentRow(
                            Kind: "succeeded",
                            When: first.PaidAt ?? first.RenewalRequest.InitiatedAt,
                            PaymentIntentId: g.Key,
                            Amount: g.Sum(p => p.RenewalRequest.Amount),
                            Abn: first.RenewalRequest.SearchResult?.SearchLog?.Abn,
                            BusinessNames: g.Select(p => p.RenewalRequest.SearchResult?.BusinessName)
                                .Where(n => n is not null).Select(n => n!).Distinct().ToList(),
                            LeadId: lead?.Id,
                            CustomerName: lead?.FullName ?? withCard.CardholderName,
                            Email: lead?.Email ?? first.RenewalRequest.Email,
                            CardBrand: withCard.CardBrand,
                            CardLast4: withCard.CardLast4,
                            CardholderName: withCard.CardholderName,
                            Error: null,
                            Renewals: g.Select(p => new PaymentRenewalRef(p.RenewalRequestId, p.RenewalRequest.Status.ToString())).ToList());
                    }));
            }

            if (!string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var fails = await db.FunnelEvents.AsNoTracking()
                    .Include(e => e.Lead)
                    .Where(e => e.Step == FunnelSteps.PaymentFailed)
                    .ToListAsync();

                rows.AddRange(fails.Select(e => new PaymentRow(
                    Kind: "failed",
                    When: e.CreatedAt,
                    PaymentIntentId: null,
                    Amount: null,
                    Abn: e.Abn,
                    BusinessNames: [],
                    LeadId: e.LeadId,
                    CustomerName: e.Lead?.FullName,
                    Email: e.Lead?.Email,
                    CardBrand: null,
                    CardLast4: null,
                    CardholderName: null,
                    Error: e.Detail,
                    Renewals: [])));
            }

            if (dateFrom.HasValue)
                rows = rows.Where(r => r.When >= dateFrom.Value).ToList();
            if (dateTo.HasValue)
            {
                var dt = dateTo.Value.AddDays(1);
                rows = rows.Where(r => r.When < dt).ToList();
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                rows = rows.Where(r =>
                    (r.Abn?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (r.Email?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (r.CustomerName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    r.BusinessNames.Any(n => n.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            rows = rows.OrderByDescending(r => r.When).ToList();

            var now = DateTime.UtcNow;
            var thirtyDaysAgo = now.AddDays(-30);
            var succeeded = rows.Where(r => r.Kind == "succeeded").ToList();
            var failed = rows.Where(r => r.Kind == "failed").ToList();

            var items = rows
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    kind = r.Kind,
                    when = r.When,
                    paymentIntentId = r.PaymentIntentId,
                    amount = r.Amount,
                    abn = r.Abn,
                    businessNames = r.BusinessNames,
                    leadId = r.LeadId,
                    customerName = r.CustomerName,
                    email = r.Email,
                    cardBrand = r.CardBrand,
                    cardLast4 = r.CardLast4,
                    cardholderName = r.CardholderName,
                    error = r.Error,
                    renewals = r.Renewals.Select(x => new { id = x.Id, status = x.Status }),
                })
                .ToList();

            return Results.Ok(new
            {
                totalCount = rows.Count,
                page,
                pageSize,
                items,
                stats = new
                {
                    succeededCount = succeeded.Count,
                    succeededValue = succeeded.Sum(r => r.Amount ?? 0m),
                    failedCount = failed.Count,
                    succeeded30d = succeeded.Count(r => r.When >= thirtyDaysAgo),
                    succeededValue30d = succeeded.Where(r => r.When >= thirtyDaysAgo).Sum(r => r.Amount ?? 0m),
                    failed30d = failed.Count(r => r.When >= thirtyDaysAgo),
                },
            });
        }).RequireAuthorization().WithTags("Admin.Payments");
    }
}
