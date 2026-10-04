using System.Data;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Services;

public interface IBusinessNameImportService
{
    /// <summary>
    /// Loads the current data.gov.au business names file into <see cref="RegisteredBusinessName"/>
    /// unless the active import already came from the same file version.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 2)]
    Task ImportIfChangedAsync(bool force, CancellationToken ct);
}

/// <summary>
/// Keeps our copy of ASIC's public business names register. The file is ~3M rows (~70MB
/// zipped); only registered names with an ABN are kept. A new import loads beside the
/// active one and is switched in with one flag, so lookups never see a half-loaded table.
/// </summary>
public class BusinessNameImportService(
    HttpClient http,
    ApplicationDbContext db,
    ILogger<BusinessNameImportService> logger) : IBusinessNameImportService
{
    public const string RecurringJobId = "business-names-import";
    private const string PackageUrl = "https://data.gov.au/data/api/3/action/package_show?id=asic-business-names";
    private const int BatchSize = 50_000;

    public async Task ImportIfChangedAsync(bool force, CancellationToken ct)
    {
        var (url, modified) = await FindCurrentZipAsync(ct);

        var active = await db.BusinessNameImports.AsNoTracking().FirstOrDefaultAsync(i => i.IsActive, ct);
        if (!force && active is not null && active.SourceUrl == url && active.SourceModified == modified)
        {
            logger.LogInformation("Business names register unchanged ({Url}, {Modified}); skipping import", url, modified);
            return;
        }

        var import = new BusinessNameImport
        {
            Id = Guid.NewGuid(),
            SourceUrl = url,
            SourceModified = modified,
            StartedAt = DateTime.UtcNow,
        };
        db.BusinessNameImports.Add(import);
        await db.SaveChangesAsync(ct);

        var zipPath = Path.Combine(Path.GetTempPath(), $"business-names-{import.Id:N}.zip");
        try
        {
            await using (var download = await http.GetStreamAsync(url, ct))
            await using (var file = File.Create(zipPath))
                await download.CopyToAsync(file, ct);

            import.RowCount = await LoadRowsAsync(zipPath, import.Id, ct);
            if (import.RowCount < 100_000)
                throw new InvalidOperationException($"Only {import.RowCount} registered names in the file; refusing to replace the current copy.");

            // One statement, so lookups switch from the old copy to the new one atomically.
            import.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE BusinessNameImports SET IsActive = CASE WHEN Id = {import.Id} THEN 1 ELSE 0 END WHERE IsActive = 1 OR Id = {import.Id}", ct);
            import.IsActive = true;
            logger.LogInformation("Imported {Count} registered business names from {Url}", import.RowCount, url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Business names import from {Url} failed", url);
            import.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            import.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
            await DeleteInactiveRowsAsync(CancellationToken.None);
        }
    }

    /// <summary>The dataset's "Current ZIP" resource; its file name changes with each release.</summary>
    private async Task<(string Url, string? Modified)> FindCurrentZipAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(PackageUrl, ct));
        foreach (var resource in doc.RootElement.GetProperty("result").GetProperty("resources").EnumerateArray())
        {
            var format = resource.TryGetProperty("format", out var f) ? f.GetString() : null;
            var url = resource.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (!string.Equals(format, "ZIP", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(url)) continue;
            var modified = resource.TryGetProperty("last_modified", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return (url, modified);
        }
        throw new InvalidOperationException("data.gov.au business names dataset has no ZIP resource.");
    }

    private async Task<int> LoadRowsAsync(string zipPath, Guid importId, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No CSV inside the business names ZIP.");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var rows = TabSeparatedReader.Read(reader).GetEnumerator();
        if (!rows.MoveNext()) throw new InvalidOperationException("Business names CSV is empty.");

        var header = rows.Current;
        int Col(string name) => header.FindIndex(h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) is var i and >= 0
            ? i : throw new InvalidOperationException($"Business names CSV has no {name} column.");
        int nameCol = Col("BN_NAME"), statusCol = Col("BN_STATUS"), regCol = Col("BN_REG_DT"),
            cancelCol = Col("BN_CANCEL_DT"), abnCol = Col("BN_ABN");
        var width = new[] { nameCol, statusCol, regCol, cancelCol, abnCol }.Max() + 1;

        var table = new DataTable();
        table.Columns.Add(nameof(RegisteredBusinessName.ImportId), typeof(Guid));
        table.Columns.Add(nameof(RegisteredBusinessName.Abn), typeof(string));
        table.Columns.Add(nameof(RegisteredBusinessName.Name), typeof(string));
        table.Columns.Add(nameof(RegisteredBusinessName.RegistrationDate), typeof(string));

        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = "RegisteredBusinessNames",
            BulkCopyTimeout = 0,
            BatchSize = BatchSize,
        };
        foreach (DataColumn c in table.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

        var total = 0;
        while (rows.MoveNext())
        {
            var r = rows.Current;
            if (r.Count < width) continue;
            if (!r[statusCol].Trim().Equals("Registered", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(r[cancelCol])) continue;
            var abn = r[abnCol].Trim();
            if (abn.Length != 11 || !abn.All(char.IsAsciiDigit)) continue;
            var name = BusinessNameText.Clean(r[nameCol]);
            if (name.Length == 0) continue;

            table.Rows.Add(importId, abn, name.Length > 200 ? name[..200] : name, Truncate(r[regCol].Trim(), 10));
            if (table.Rows.Count >= BatchSize)
            {
                await bulk.WriteToServerAsync(table, ct);
                total += table.Rows.Count;
                table.Clear();
            }
        }
        if (table.Rows.Count > 0)
        {
            await bulk.WriteToServerAsync(table, ct);
            total += table.Rows.Count;
        }
        return total;
    }

    /// <summary>Clears rows of superseded and failed imports, in batches to keep transactions small.</summary>
    private async Task DeleteInactiveRowsAsync(CancellationToken ct)
    {
        try
        {
            var activeId = await db.BusinessNameImports.Where(i => i.IsActive).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
            if (activeId is null) return;
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            int deleted;
            do
            {
                deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE TOP (50000) FROM RegisteredBusinessNames WHERE ImportId <> {activeId.Value}", ct);
            } while (deleted > 0);
        }
        catch (Exception ex)
        {
            // Leftover rows are invisible to lookups; the next import tries again.
            logger.LogWarning(ex, "Could not clear old business name rows");
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Reads data.gov.au's business names file: tab-separated, with CSV-style quoting — a field
/// that contains a quote is wrapped in quotes and the quote doubled (<c>"""A"" CLASS"</c>
/// is <c>"A" CLASS</c>). Splitting on tabs alone leaves those doubled quotes in the names.
/// </summary>
public static class TabSeparatedReader
{
    public static IEnumerable<List<string>> Read(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;
        int c;
        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '"' when atFieldStart:
                    inQuotes = true;
                    atFieldStart = false;
                    break;
                case '\t':
                    fields.Add(field.ToString());
                    field.Clear();
                    atFieldStart = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    atFieldStart = true;
                    yield return fields;
                    fields = [];
                    break;
                default:
                    field.Append(ch);
                    atFieldStart = false;
                    break;
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}

public static class BusinessNameText
{
    /// <summary>Trims and collapses runs of whitespace; the register has names with leading spaces.</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var decoded = System.Net.WebUtility.HtmlDecode(name);
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Matching key between ASIC's renewal list and the public register: letters and digits
    /// only, upper-cased, so quoting, apostrophes, ampersand encoding and spacing don't matter.
    /// </summary>
    public static string MatchKey(string? name)
    {
        var cleaned = Clean(name);
        var sb = new StringBuilder(cleaned.Length);
        foreach (var ch in cleaned)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }
}
