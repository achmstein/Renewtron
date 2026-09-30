namespace Renewtron.Settings;

public class OntraportSettings
{
    public string ApiAppId { get; set; }
    public string ApiKey { get; set; }
    public string ConversationId { get; set; }
    public int DefaultPollingTimeoutSeconds { get; set; } = 60;

    /// <summary>Create/update an Ontraport contact for everyone who fills in the Renewtron wizard.</summary>
    public bool PushWizardContacts { get; set; } = true;

    /// <summary>Tag added when the wizard form is filled in. Default 1908 "completed Renew a Business Name v3.0 step 1"; blank = no tag.</summary>
    public string? WizardLeadTagId { get; set; } = "1908";

    /// <summary>Tag added when a wizard renewal is paid for. Default 1737 "business name paid"; blank = no tag.</summary>
    public string? WizardPaidTagId { get; set; } = "1737";
}
