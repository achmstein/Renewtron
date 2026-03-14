namespace Renewtron.Settings;

public class AirwallexSettings
{
    public string ClientId { get; set; } = null!;
    public string ApiKey { get; set; } = null!;
    public string Environment { get; set; } = "demo"; // "demo" or "prod"

    public string BaseUrl => Environment == "prod"
        ? "https://api.airwallex.com"
        : "https://api-demo.airwallex.com";

    public string JsEnvironment => Environment == "prod" ? "prod" : "demo";
}
