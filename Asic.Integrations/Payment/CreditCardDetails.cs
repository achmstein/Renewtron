namespace Asic.Integrations.Payment;

public class CreditCardDetails
{
    public required string CardNumber { get; set; }
    public required string CardholderName { get; set; }
    public required string ExpiryMonth { get; set; }
    public required string ExpiryYear { get; set; }
    public required string Cvc { get; set; }
}