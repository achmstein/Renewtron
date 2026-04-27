namespace Renewtron.Settings;

public class PricingSettings
{
    // These represent the total customer prices for renewals
    public decimal OneYearFee { get; set; }
    public decimal ThreeYearFee { get; set; }

    // Deprecated - kept for backward compatibility but not used
    public decimal MarkupPercentage { get; set; }

    /// <summary>
    /// Gets the price charged to the customer
    /// </summary>
    public decimal GetCustomerPrice(int years)
    {
        return years == 1 ? OneYearFee : ThreeYearFee;
    }

    /// <summary>
    /// Gets the ASIC fee (same as customer price for automated renewals)
    /// For manual renewals, admin can specify a different ASIC fee if needed
    /// </summary>
    public decimal GetAsicFee(int years)
    {
        return years == 1 ? OneYearFee : ThreeYearFee;
    }
}