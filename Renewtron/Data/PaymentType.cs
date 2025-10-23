namespace Renewtron.Data;

/// <summary>
/// Represents the type of payment for a renewal request
/// </summary>
public enum PaymentType
{
    /// <summary>
    /// Payment processed through Stripe
    /// </summary>
    Stripe = 0,

    /// <summary>
    /// External payment (cash, check, wire transfer, etc.)
    /// </summary>
    External = 1
}
