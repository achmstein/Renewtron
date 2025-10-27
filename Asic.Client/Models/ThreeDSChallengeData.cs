namespace Asic.Client.Models;

public class ThreeDSChallengeData
{
    public string ChallengeUrl { get; set; }
    public string ThreeDSSessionData { get; set; }
    public string CReq { get; set; }
    public string TransactionId { get; set; }
    public string IssuerId { get; set; }
}