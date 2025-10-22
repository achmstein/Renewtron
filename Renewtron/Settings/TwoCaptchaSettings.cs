namespace Renewtron.Settings;

public class TwoCaptchaSettings
{
    public string ApiKey { get; set; }
    public int DefaultTimeout { get; set; }
    public int RecaptchaTimeout { get; set; }
    public int PollingInterval { get; set; }
}
