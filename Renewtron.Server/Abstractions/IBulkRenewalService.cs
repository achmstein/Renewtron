using Renewtron.Data;

namespace Renewtron.Abstractions;

public interface IBulkRenewalService
{
    Task<BulkUploadResult> UploadAsync(Stream fileStream, string fileName);
    Task ProcessEligibleRenewalsAsync();
}

public class BulkUploadResult
{
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedDuplicates { get; set; }
    public int SkippedInvalid { get; set; }
    public List<string> Errors { get; set; } = new();
}
