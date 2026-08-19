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
            string? cardBrand = null,
            string? search = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            int page = 1,
            int pageSize = 20) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            // Facet params accept comma-separated multi-values ("Succeeded,Failed"), matched
            // case-insensitively — the old single-value "succeeded"/"failed" parses identically.
            // Unknown values are ignored; both (or neither) result selected means no filter.
            var resultValues = (status ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .Where(s => s is "succeeded" or "failed")
                .Distinct()
                .ToArray();
            var cardBrandValues = (cardBrand ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Both kinds are always loaded: the succeeded/failed merge happens in memory, and
            // the Result facet needs the other kind's count even when only one is selected.
            var allRows = new List<PaymentRow>();

            var payments = await db.StripePayments.AsNoTracking()
                .Include(p => p.RenewalRequest).ThenInclude(r => r.SearchResult).ThenInclude(s => s.SearchLog)
                .Include(p => p.RenewalRequest).ThenInclude(r => r.Lead)
                .Where(p => p.PaymentStatus == "succeeded")
                .ToListAsync();

            allRows.AddRange(payments
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

            var fails = await db.FunnelEvents.AsNoTracking()
                .Include(e => e.Lead)
                .Where(e => e.Step == FunnelSteps.PaymentFailed)
                .ToListAsync();

            allRows.AddRange(fails.Select(e => new PaymentRow(
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

            // One filter pipeline, with each facet dimension excludable so its own option
            // counts can be computed under the OTHER selections (classic faceted search).
            List<PaymentRow> Filtered(bool applyResult = true, bool applyCardBrand = true)
            {
                IEnumerable<PaymentRow> q = allRows;
                if (dateFrom.HasValue)
                    q = q.Where(r => r.When >= dateFrom.Value);
                if (dateTo.HasValue)
                {
                    var dt = dateTo.Value.AddDays(1);
                    q = q.Where(r => r.When < dt);
                }
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim();
                    q = q.Where(r =>
                        (r.Abn?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (r.Email?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (r.CustomerName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        r.BusinessNames.Any(n => n.Contains(s, StringComparison.OrdinalIgnoreCase)));
                }
                if (applyResult && resultValues.Length > 0)
                    q = q.Where(r => resultValues.Contains(r.Kind));
                if (applyCardBrand && cardBrandValues.Length > 0)
                    q = q.Where(r => r.CardBrand != null &&
                        cardBrandValues.Any(v => string.Equals(v, r.CardBrand, StringComparison.OrdinalIgnoreCase)));
                return q.ToList();
            }

            // Each facet's counts are computed with its own dimension excluded; count-desc,
            // zero-count options omitted (a group is never empty).
            var facets = new
            {
                result = Filtered(applyResult: false)
                    .GroupBy(r => r.Kind == "succeeded" ? "Succeeded" : "Failed")
                    .Select(g => new { value = g.Key, count = g.Count() })
                    .OrderByDescending(f => f.count)
                    .ToList(),
                cardBrand = Filtered(applyCardBrand: false)
                    .Where(r => !string.IsNullOrWhiteSpace(r.CardBrand))
                    .GroupBy(r => NormalizeBrand(r.CardBrand!))
                    .Select(g => new { value = g.Key, count = g.Count() })
                    .OrderByDescending(f => f.count)
                    .ToList(),
            };

            var rows = Filtered().OrderByDescending(r => r.When).ToList();

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
                facets,
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

    /// <summary>Stripe reports brands lowercase ("visa"); facet values read capitalized.</summary>
    private static string NormalizeBrand(string brand)
    {
        var b = brand.Trim();
        return b.Length == 0 ? b : char.ToUpperInvariant(b[0]) + b[1..].ToLowerInvariant();
    }
}
