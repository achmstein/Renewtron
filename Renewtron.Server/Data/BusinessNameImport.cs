namespace Renewtron.Data;

/// <summary>A load of the data.gov.au business names file into <see cref="RegisteredBusinessName"/>.</summary>
public class BusinessNameImport
{
    public Guid Id { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    /// <summary>The resource's last_modified on data.gov.au; an unchanged value means nothing new to import.</summary>
    public string? SourceModified { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int RowCount { get; set; }
    /// <summary>The one import lookups read from. Set only once its rows are fully loaded.</summary>
    public bool IsActive { get; set; }
    public string? Error { get; set; }
}
