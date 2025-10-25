namespace Asic.Client.Models;

public class BusinessNamesSearchResult
{
    public bool Success { get; set; }
    public List<BusinessName> BusinessNames { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public static BusinessNamesSearchResult Succeeded(List<BusinessName> businessNames)
    {
        return new BusinessNamesSearchResult
        {
            Success = true,
            BusinessNames = businessNames
        };
    }

    public static BusinessNamesSearchResult Failed(string errorMessage)
    {
        return new BusinessNamesSearchResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
