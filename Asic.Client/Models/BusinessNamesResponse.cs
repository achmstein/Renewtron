namespace Asic.Client.Models;

public class BusinessNamesResponse
{
    private BusinessNamesResponse() { }

    public bool Succeeded { get; set; }
    public List<BusinessName> BusinessNames { get; set; }

    public static BusinessNamesResponse Success(List<BusinessName> businessNames)
    {
        return new BusinessNamesResponse
        {
            Succeeded = true,
            BusinessNames = businessNames,
        };
    }

    public static BusinessNamesResponse Failure()
    {
        return new BusinessNamesResponse { Succeeded = false };
    }
}
