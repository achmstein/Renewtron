namespace Renewtron.Data;

public class SavedCreditCard
{
    public Guid Id { get; set; }
    public string CardholderName { get; set; }
    public string CardNumberLast4 { get; set; }
    public string? CardBrand { get; set; }
    public string ExpiryMonth { get; set; }
    public string ExpiryYear { get; set; }

    // Encrypted card data (use encryption in production!)
    public string EncryptedCardNumber { get; set; }
    public string EncryptedCvc { get; set; }

    // Metadata
    public DateTime CreatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }
}