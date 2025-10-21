namespace Renewtron.Settings;

public class PricingSettings
{
    public decimal OneYearFee { get; set; }
    public decimal ThreeYearFee { get; set; }
    public decimal MarkupPercentage { get; set; }

    public decimal GetCustomerPrice(int years)
    {
        var asicFee = years == 1 ? OneYearFee : ThreeYearFee;
        return asicFee * (1 + MarkupPercentage / 100);
    }

    public decimal GetAsicFee(int years)
    {
        return years == 1 ? OneYearFee : ThreeYearFee;
    }
}