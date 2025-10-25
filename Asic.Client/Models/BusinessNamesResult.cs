namespace Asic.Client.Models;

public class BusinessNamesResult
{
    public bool Success { get; set; }
    public List<BusinessName> BusinessNames { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public static BusinessNamesResult Succeeded(List<BusinessName> businessNames)
    {
        return new BusinessNamesResult
        {
            Success = true,
            BusinessNames = businessNames
        };
    }

    public static BusinessNamesResult Failed(string errorMessage)
    {
        return new BusinessNamesResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
