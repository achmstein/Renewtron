namespace Renewtron.Data;

/// <summary>
/// An email Renewtron sends whose wording the admin can change (see <see cref="Services.Email.EmailTemplateCatalog"/>
/// for the list and each one's default). There's a row only once someone has switched it off or saved a
/// version; without one the email is on and uses the wording in code.
/// </summary>
public class EmailTemplate
{
    /// <summary>The catalogue key, e.g. "renewal confirmation" (the same name the email log shows).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Off: Renewtron skips this email instead of sending it.</summary>
    public bool Enabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Where a version came from.</summary>
public enum EmailTemplateVersionSource
{
    /// <summary>Saved from the editor.</summary>
    Edit = 0,
    /// <summary>"Reset to default": a copy of the wording in code at the time.</summary>
    Default = 1,
    /// <summary>A copy of an earlier version, restored.</summary>
    Rollback = 2,
}

/// <summary>
/// One saved version of a template. Versions are never changed or deleted: saving, resetting and rolling
/// back each add one, and the highest number is what's sent. Version 0 means the wording in code (no row).
/// </summary>
public class EmailTemplateVersion
{
    public int Id { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public int Version { get; set; }

    /// <summary>Liquid, no HTML.</summary>
    public string Subject { get; set; } = string.Empty;
    /// <summary>The title in the coloured band at the top. Liquid, no HTML. Empty for no band.</summary>
    public string Heading { get; set; } = string.Empty;
    /// <summary>HTML with Liquid: the message itself, which goes inside the shared layout.</summary>
    public string Body { get; set; } = string.Empty;

    public EmailTemplateVersionSource Source { get; set; }
    /// <summary>For a rollback, the version it restored.</summary>
    public int? RestoredFrom { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
