namespace Asic.Client.Models;

public class BusinessName
{
    public string Name { get; set; }
    public string Status { get; set; }
    public string RegistrationDate { get; set; }
    public string RenewalDate { get; set; }
    public string CancelledDate { get; set; }
    public string CancellationUnderReview { get; set; }
    public string AddressForServiceDocuments { get; set; }
    public string PrincipalPlaceOfBusiness { get; set; }
    public List<Holder> Holders { get; set; } = [];
}
