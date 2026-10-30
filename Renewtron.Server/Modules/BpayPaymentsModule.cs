using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// ASIC's BPAY bills for renewals lodged to pay later, paid through Airwallex. Staff tick
/// the bills, confirm the total, and each becomes one Airwallex transfer; the sync job marks
/// the renewal paid once Airwallex reports PAID.
/// </summary>
public sealed class BpayPaymentsModule : ICarterModule
{
    public sealed record PayRequest(List<Guid> RenewalIds, decimal ExpectedTotal);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/bpay").RequireAuthorization().WithTags("Admin.Bpay");

        group.MapGet("/", async (ApplicationDbContext db, IOptionsSnapshot<AsicSettings> asic,
            IOptionsSnapshot<AirwallexSettings> airwallex, bool includePaid = false) =>
        {
            var paidFrom = DateTime.UtcNow.AddDays(-30);
            var renewals = await db.RenewalRequests.AsNoTracking()
                .Include(r => r.SearchResult).ThenInclude(s => s.SearchLog)
                .Where(r => r.BpayReference != null && !r.IsTest)
                .Where(r => r.BpayPaidAt == null || (includePaid && r.BpayPaidAt >= paidFrom))
                .OrderBy(r => r.CompletedAt ?? r.InitiatedAt)
                .ToListAsync();

            var fees = asic.Value;
            var items = renewals.Select(r => new
            {
                id = r.Id,
                businessName = r.SearchResult?.BusinessName,
                abn = r.SearchResult?.SearchLog?.Abn,
                renewalYears = r.RenewalYears,
                lodgedAt = r.CompletedAt ?? r.LastAttemptAt ?? r.InitiatedAt,
                transactionReference = r.TransactionReference,
                billerCode = r.BpayBillerCode,
                customerReference = r.BpayReference,
                // What was sent, or what would be sent now.
                amount = r.BpayAmount is { } sent && r.AirwallexTransferStatus is not null && !BpayPaymentService.Ended.Contains(r.AirwallexTransferStatus)
                    ? sent
                    : fees.FeeFor(r.RenewalYears),
                payable = BpayPaymentService.IsPayable(r),
                transferId = r.AirwallexTransferId,
                transferStatus = r.AirwallexTransferStatus,
                error = r.BpayPaymentError,
                paidAt = r.BpayPaidAt,
            }).ToList();

            return Results.Ok(new
            {
                items,
                airwallexReady = airwallex.Value.Ready,
                sandbox = airwallex.Value.UseSandbox,
                oneYearFee = fees.FeeFor(1),
                threeYearFee = fees.FeeFor(3),
            });
        });

        group.MapPost("/pay", async (PayRequest body, IBpayPaymentService payments) =>
        {
            var (outcomes, refused) = await payments.PayAsync(body.RenewalIds.Distinct().ToList(), body.ExpectedTotal);
            return refused is not null
                ? Results.Conflict(new { error = refused })
                : Results.Ok(new { outcomes });
        });

        group.MapPost("/sync", async (IBpayPaymentService payments) =>
            Results.Ok(new { changed = await payments.SyncAsync() }));
    }
}
