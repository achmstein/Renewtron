using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Tests;

/// <summary>What staff can do to a paid renewal, alone or in bulk (RenewalOrderActions).</summary>
public class RenewalOrderActionsTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    private static (ApplicationDbContext Db, RenewalOrder Order, OntraportSale Sale) Seed(
        RenewalOrderStage stage = RenewalOrderStage.NeedsPerson, RenewalOrderReason? reason = RenewalOrderReason.NoDueDate)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sale = new OntraportSale
        {
            Id = Guid.NewGuid(), OntraportContactId = "c1", OntraportTransactionId = "t1", ContactName = "Bob Builder", Email = "bob@example.com", MobileNumber = "0",
            BusinessName = "BOB BUILDS", Abn = "22222222222", RenewalYears = 1, AmountPaid = 199, SyncedAt = DateTime.UtcNow,
            Status = OntraportSaleStatus.Synced, ErrorMessage = OntraportSalesService.NoDueDateMessage,
        };
        var order = new RenewalOrder
        {
            Id = Guid.NewGuid(), Source = RenewalOrderSource.Ontraport, SourceId = sale.Id, Abn = sale.Abn, BusinessName = sale.BusinessName,
            NameKey = "BOBBUILDS", Years = 1, AmountPaid = 199, PaidAt = DateTime.UtcNow, Stage = stage, Reason = reason,
        };
        db.AddRange(sale, order);
        db.SaveChanges();
        return (db, order, sale);
    }

    [Theory]
    [InlineData(10, "The next processing run")]
    [InlineData(90, "when ASIC opens the window")]
    [InlineData(-60, "too late to renew online")]
    public async Task A_due_date_a_person_found_goes_on_the_sale_and_says_what_happens_next(int dueInDays, string says)
    {
        var (db, order, sale) = Seed();
        var r = await RenewalOrderActions.SetDueDateAsync(db, order, Today.AddDays(dueInDays), Today, CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Contains(says, r.Message);
        Assert.Equal(Today.AddDays(dueInDays), DateOnly.FromDateTime(sale.RenewalDueDate!.Value));
        Assert.Null(sale.ErrorMessage);
        Assert.Equal(OntraportSaleStatus.Synced, sale.Status); // back for the processing run to decide
    }

    [Fact]
    public async Task A_due_date_is_refused_when_it_cant_be_right_or_the_renewal_is_finished()
    {
        var (db, order, _) = Seed();
        Assert.False((await RenewalOrderActions.SetDueDateAsync(db, order, Today.AddYears(-20), Today, CancellationToken.None)).Ok);

        var (db2, renewed, _) = Seed(RenewalOrderStage.Renewed, null);
        Assert.False((await RenewalOrderActions.SetDueDateAsync(db2, renewed, Today, Today, CancellationToken.None)).Ok);
    }

    [Fact]
    public void A_decision_needs_a_note_and_an_open_renewal()
    {
        var (_, order, _) = Seed(RenewalOrderStage.NeedsPerson, RenewalOrderReason.TooLate);
        Assert.False(RenewalOrderActions.Decide(order, RenewalOrderReason.RenewedElsewhere, " ", null, "staff").Ok);
        Assert.False(RenewalOrderActions.Decide(order, RenewalOrderReason.TooLate, "x", null, "staff").Ok); // not a person's outcome

        var ok = RenewalOrderActions.Decide(order, RenewalOrderReason.RenewedElsewhere, "ASIC shows due 2027", null, "staff");
        Assert.True(ok.Ok);
        Assert.Equal((RenewalOrderReason.RenewedElsewhere, OrderEvidenceSource.Staff, "staff"), (order.Outcome!.Value, order.EvidenceSource!.Value, order.OutcomeBy));

        var (_, renewing, _) = Seed(RenewalOrderStage.Renewing, null);
        Assert.False(RenewalOrderActions.Decide(renewing, RenewalOrderReason.Refunded, "x", null, "staff").Ok);
    }

    [Fact]
    public void Undo_clears_a_decision_and_says_when_there_is_none()
    {
        var (_, order, _) = Seed(RenewalOrderStage.NeedsPerson, RenewalOrderReason.TooLate);
        Assert.False(RenewalOrderActions.Reopen(order).Ok);
        RenewalOrderActions.Decide(order, RenewalOrderReason.RegisterAgain, "Told the client", null, "staff");
        Assert.True(RenewalOrderActions.Reopen(order).Ok);
        Assert.Null(order.Outcome);
    }
}
