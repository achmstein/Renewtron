using Renewtron.Data;

namespace Renewtron.Tests;

/// <summary>The error classifier against real messages from production (Nov 2026).</summary>
public class RenewalErrorClassifierTests
{
    private const string Complete = "Complete Payment Action";

    [Theory]
    // ASIC declined our card at its last step: nothing taken, retryable.
    [InlineData(Complete, "Card declined at ASIC: Your transaction has been declined. Please contact your financial institution.. Transaction reference: 1-10KSGAGZ", RenewalErrorCategories.CardDeclined)]
    [InlineData(Complete, "Payment processed but completion failed: Your transaction has been declined. Please contact your financial institution. You may wish to attempt the transaction again.. Transaction reference: 1-10KSGAGZ", RenewalErrorCategories.CardDeclined)]
    [InlineData("Process Payment", "Renewal initiated but payment failed: Your card was declined.", RenewalErrorCategories.CardDeclined)]
    // Unclear whether ASIC took the money: never retried automatically.
    [InlineData(Complete, "Payment processed but completion failed: Payment completion failed: An error occurred while sending the request.. Transaction reference: 1-10FLYDU8", RenewalErrorCategories.PaymentRisk)]
    [InlineData(Complete, "Payment processed but completion failed: Unknown payment status. Transaction reference: 1-10QQIF3H", RenewalErrorCategories.PaymentRisk)]
    [InlineData(Complete, "This business name is not due for renewal yet.", RenewalErrorCategories.PaymentRisk)]
    [InlineData("Submit Pay Later", "Couldn't confirm the BPAY details", RenewalErrorCategories.PaymentRisk)]
    // Before payment.
    [InlineData("Already Processing", "A renewal for this ABN is already in progress. Please complete or cancel the existing renewal before starting a new one.", RenewalErrorCategories.AlreadyInProgress)]
    [InlineData("Select Business Name", "This business name is not due for renewal yet. ASIC will send a renewal notice when it becomes due.", RenewalErrorCategories.NotDueYet)]
    [InlineData("Search", "Business name 'ANYMAL' not found under ABN 64616730888 at ASIC", RenewalErrorCategories.Terminal)]
    [InlineData("Initialize Session", "Failed to initialize renewal session", RenewalErrorCategories.Transient)]
    [InlineData("Search", "Search failed: An error occurred while sending the request.", RenewalErrorCategories.Transient)]
    public void Classifies_real_messages(string step, string message, string expected) =>
        Assert.Equal(expected, RenewalErrorClassifier.Classify(step, message));

    [Fact]
    public void Card_declined_never_holds_payment_but_payment_risk_does()
    {
        Assert.False(RenewalErrorClassifier.MayHoldPayment(Complete, null, RenewalErrorCategories.CardDeclined));
        Assert.False(RenewalErrorClassifier.MayHoldPayment(Complete, "token", RenewalErrorCategories.CardDeclined));
        Assert.True(RenewalErrorClassifier.MayHoldPayment(Complete, null, RenewalErrorCategories.PaymentRisk));
        Assert.True(RenewalErrorClassifier.MayHoldPayment("Search", "token", RenewalErrorCategories.Transient));
        Assert.False(RenewalErrorClassifier.MayHoldPayment("Search", null, RenewalErrorCategories.Transient));
    }

    [Theory]
    [InlineData(RenewalErrorCategories.NotDueYet, OntraportSaleStatus.AsicNotYetDue)]
    [InlineData(RenewalErrorCategories.AlreadyInProgress, OntraportSaleStatus.RenewalInProgress)]
    [InlineData(RenewalErrorCategories.PaymentRisk, OntraportSaleStatus.RenewalFailed)]
    [InlineData(RenewalErrorCategories.CardDeclined, OntraportSaleStatus.RenewalFailed)]
    public void Sale_status_follows_the_renewal_category(string category, OntraportSaleStatus expected) =>
        Assert.Equal(expected, OntraportSaleStatusClassifier.FromRenewalCategory(category));
}
