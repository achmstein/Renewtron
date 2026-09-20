using System.Text.Json;

namespace Asic.KeyTool;

/// <summary>One thing this machine asked ASIC for, kept so the same sale isn't asked twice.</summary>
public sealed class HistoryEntry
{
    /// <summary>Ontraport contact the sale came from; empty for a hand-typed enquiry.</summary>
    public string ContactId { get; set; } = "";
    public string BusinessName { get; set; } = "";
    public string Abn { get; set; } = "";
    /// <summary>The renewal this belonged to — the same name comes round again next year.</summary>
    public DateTime? RenewalDueDate { get; set; }
    public bool Submitted { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Error { get; set; }
    public int CaptchaSolves { get; set; }
    public DateTime At { get; set; }
}

/// <summary>
/// The tool's memory, in %APPDATA%\Renewtron\asic-keytool-history.json. Renewtron's database
/// used to be what stopped a sale being sent to ASIC twice; on a desktop this file does it.
/// Only successful requests count as done — a failed one comes back around next time.
/// </summary>
public sealed class History
{
    public List<HistoryEntry> Entries { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Renewtron", "asic-keytool-history.json");

    public static History Load()
    {
        try
        {
            if (!File.Exists(Path)) return new History();
            return JsonSerializer.Deserialize<History>(File.ReadAllText(Path), JsonOptions) ?? new History();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new History();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>True once ASIC has accepted a request for this renewal.</summary>
    public bool AlreadyRequested(Sale sale) =>
        Entries.Any(e => e.Submitted && IsSameRenewal(e, sale));

    /// <summary>An earlier failed attempt for this renewal, so the list can say "tried before".</summary>
    public HistoryEntry? LastFailure(Sale sale) =>
        Entries.Where(e => !e.Submitted && IsSameRenewal(e, sale))
            .OrderByDescending(e => e.At)
            .FirstOrDefault();

    /// <summary>
    /// Same contact (or, for a hand-typed one, the same business name) <em>and</em> the same
    /// renewal due date. The due date is what stops last year's request from hiding this
    /// year's: a business name renews annually against the same Ontraport contact, and the
    /// due date moves on once it's renewed.
    /// </summary>
    private static bool IsSameRenewal(HistoryEntry entry, Sale sale) =>
        entry.RenewalDueDate == sale.RenewalDueDate &&
        (entry.ContactId.Length > 0 && entry.ContactId == sale.ContactId ||
         string.Equals(entry.BusinessName.Trim(), sale.BusinessName.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Record(Enquiry enquiry)
    {
        Entries.Add(new HistoryEntry
        {
            ContactId = enquiry.ContactId,
            BusinessName = enquiry.BusinessName,
            Abn = enquiry.Abn,
            RenewalDueDate = enquiry.RenewalDueDate,
            Submitted = enquiry.Submitted,
            ReferenceNumber = enquiry.ReferenceNumber,
            Error = enquiry.Error,
            CaptchaSolves = enquiry.CaptchaSolves,
            At = DateTime.UtcNow,
        });
    }
}
