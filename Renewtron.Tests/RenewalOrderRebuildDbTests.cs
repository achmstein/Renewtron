using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Xunit.Abstractions;

namespace Renewtron.Tests;

/// <summary>
/// Runs the order rebuild against a real database. Skipped unless RENEWTRON_TEST_DB holds a
/// connection string (the local dev SQL container). Dry run first, then for real, then twice to
/// check a second rebuild changes nothing.
/// </summary>
public class RenewalOrderRebuildDbTests(ITestOutputHelper output)
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("RENEWTRON_TEST_DB");

    [Fact]
    public async Task Rebuild_against_the_dev_database()
    {
        if (string.IsNullOrEmpty(Connection)) return;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Connection).Options;

        async Task<RenewalOrderRebuildReport> Run(bool dryRun)
        {
            await using var db = new ApplicationDbContext(options);
            var report = await new RenewalOrderService(db, NullLogger<RenewalOrderService>.Instance).RebuildAsync(dryRun, CancellationToken.None);
            output.WriteLine($"{(dryRun ? "DRY" : "REAL")}: orders {report.Orders}, created {report.Created}, changed {report.StageChanged}, linked {report.AttemptsLinked}, orphans {report.OrphanAttemptsAttached}, unmatched {report.AttemptsWithoutOrder}, duplicates {report.OpenDuplicates}");
            foreach (var b in report.ByStage) output.WriteLine($"  {b.Stage,-12} {b.Reason,-20} {b.Count,5}  {b.Amount,10:N0}");
            return report;
        }

        var dry = await Run(true);
        var real = await Run(false);
        var again = await Run(false);

        Assert.Equal(dry.Orders, real.Orders);
        Assert.Equal(0, again.Created);
        Assert.Equal(0, again.StageChanged);
        await using (var db = new ApplicationDbContext(options))
        {
            Assert.Equal(real.Orders, await db.RenewalOrders.CountAsync());
            // Every paid, non-test run is on an order unless it couldn't be matched.
            var unlinked = await db.RenewalRequests.CountAsync(r => !r.IsTest && r.RenewalOrderId == null
                && (r.PaymentType == PaymentType.External || (r.StripePayment != null && r.StripePayment.PaymentStatus == "succeeded")));
            Assert.Equal(real.AttemptsWithoutOrder, unlinked);
        }
    }
}
