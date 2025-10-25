namespace Asic.Client.Models;

public class BusinessNamesSearchResult
{
    public bool Success { get; set; }
    public List<SimplifiedBusinessName> BusinessNames { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public static BusinessNamesSearchResult Succeeded(List<SimplifiedBusinessName> businessNames)
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

public class SimplifiedBusinessName
{
    public string Name { get; set; }
    public string AccountNumber { get; set; }
    public string RegistrationDate { get; set; }
}
