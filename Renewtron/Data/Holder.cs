namespace Renewtron.Data;

public class Holder
{
    public Guid Id { get; set; }
    public Guid SearchResultId { get; set; }
    public string Name { get; set; }
    public string? Type { get; set; }
    public string? Abn { get; set; }

    // Navigation property
    public SearchResult SearchResult { get; set; }
}
