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
    public SearchInitiator InitiatedBy { get; set; }

    public SearchSource Source { get; set; }

    /// <summary>
    /// When a <see cref="SearchSource.Local"/> search was confirmed against ASIC (account
    /// numbers filled in, names ASIC won't renew marked unavailable). Null while pending.
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Last reason the ASIC check didn't complete; it is retried.</summary>
    public string? VerificationError { get; set; }

    /// <summary>True once the names can be paid for: an ASIC search, or a local one ASIC has confirmed.</summary>
    public bool IsVerified => Source == SearchSource.Asic || VerifiedAt is not null;

    public List<SearchResult> Results { get; set; } = [];
}
