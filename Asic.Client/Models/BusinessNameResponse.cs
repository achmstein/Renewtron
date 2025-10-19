namespace Asic.Client.Models;

public class BusinessNameResponse
{
    private BusinessNameResponse() { }

    public bool Succeeded { get; set; }
    public BusinessName BusinessName { get; set; }
    public bool HasMultipleBusinessNames { get; set; }

    public static BusinessNameResponse Success(BusinessName businessName, bool hasMultipleBusinessNames)
    {
        return new BusinessNameResponse
        {
            Succeeded = true,
            BusinessName = businessName,
            HasMultipleBusinessNames = hasMultipleBusinessNames
        };
    }

    public static BusinessNameResponse Failure()
    {
        return new BusinessNameResponse { Succeeded = false };
    }
}
