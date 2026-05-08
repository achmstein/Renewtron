namespace Renewtron.Settings;

public class WinBackSettings
{
    /// <summary>Subject line. Supports merge tags: {{FullName}}, {{Abn}}, {{BusinessName}}.</summary>
    public string Subject { get; set; } = "Your business name is still due for renewal — finish in 90 seconds";

    /// <summary>Plain-text body. Supports the same merge tags as Subject.</summary>
    public string BodyPlain { get; set; } =
        "Hi {{FullName}},\n\n" +
        "We noticed you started a renewal for ABN {{Abn}} but didn't complete it. " +
        "Your business name is still available to renew today — it only takes about 90 seconds.\n\n" +
        "Click here to finish: https://businessnames.applyforanabn.au/\n\n" +
        "If you've already renewed elsewhere, you can ignore this email.\n\n" +
        "— Renewtron\n";

    /// <summary>HTML body. Supports the same merge tags. If empty, BodyPlain is auto-wrapped.</summary>
    public string BodyHtml { get; set; } = string.Empty;
}
