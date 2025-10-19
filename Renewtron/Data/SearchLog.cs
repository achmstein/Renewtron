using Asic.Client.Models;

namespace Renewtron.Data;

public class SearchLog
{
    public Guid Id { get; set; }
    public string Abn { get; set; }
    public DateTime SearchedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? SessionId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int ResultsCount { get; set; }

    public List<SearchResult> Results { get; set; } = [];
}
