namespace Renewtron.Settings;

public class OntraportSettings
{
    public string ApiAppId { get; set; }
    public string ApiKey { get; set; }
    public string ContactId { get; set; }
    public int DefaultPollingTimeoutSeconds { get; set; } = 60;
}
