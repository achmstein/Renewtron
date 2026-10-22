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
}

public static class AsicPaymentMethods
{
    public const string Card = "Card";
    public const string Bpay = "Bpay";

    public static bool IsBpay(string? method) => string.Equals(method?.Trim(), Bpay, StringComparison.OrdinalIgnoreCase);
}
