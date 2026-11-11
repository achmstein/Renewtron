namespace Asic.Integrations.Models;

public class CreditCardDetails
{
    public string CardNumber { get; set; }
    public string CardholderName { get; set; }
    public string ExpiryMonth { get; set; }
    public string ExpiryYear { get; set; }
    public string Cvc { get; set; }
}