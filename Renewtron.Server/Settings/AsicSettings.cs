namespace Renewtron.Settings;

public class AsicSettings
{
    public bool ForceFallback { get; set; }
    public string Email { get; set; }
    public string CardNumber { get; set; }
    public string CardholderName { get; set; }
    public string ExpiryMonth { get; set; }
    public string ExpiryYear { get; set; }
    public string Cvc { get; set; }

    /// <summary>
    /// How renewals are paid at ASIC: "Card" pays now with the card above; "Bpay" picks
    /// "Pay later -> BPAY", lodging the renewal and recording the biller code and customer
    /// reference on it, for someone to pay at the bank.
    /// </summary>
    public string PaymentMethod { get; set; } = AsicPaymentMethods.Card;

    /// <summary>
    /// Paying by card: after this many failed card attempts on a renewal (declined, or the
    /// payment didn't go through), the next run pays later by BPAY instead. 0 = never.
    /// </summary>
    public int BpayAfterCardFailures { get; set; }

    public const int MaxBpayAfterCardFailures = 5;

    /// <summary>What ASIC charges for a 1-year renewal: the amount paid on its BPAY bill. 0 = not set.</summary>
    public decimal OneYearFee { get; set; }

    /// <summary>What ASIC charges for a 3-year renewal. 0 = not set.</summary>
    public decimal ThreeYearFee { get; set; }

    /// <summary>ASIC's fee for this renewal period, or null when it isn't set.</summary>
    public decimal? FeeFor(int years)
    {
        var fee = years == 3 ? ThreeYearFee : years == 1 ? OneYearFee : 0m;
        return fee > 0 ? fee : null;
    }
}

public static class AsicPaymentMethods
{
    public const string Card = "Card";
    public const string Bpay = "Bpay";

    public static bool IsBpay(string? method) => string.Equals(method?.Trim(), Bpay, StringComparison.OrdinalIgnoreCase);
}
