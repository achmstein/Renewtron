using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Modules;

namespace Renewtron.Services;

public interface IRenewedElsewhereService
{
    /// <summary>
    /// Marks handled the failed sales and renewals whose business name ASIC still lists as
    /// registered well after it was due: someone renewed it outside Renewtron. With
    /// <paramref name="dryRun"/> it only counts.
    /// </summary>
    Task<RenewedElsewhereResult> MarkAsync(bool dryRun, CancellationToken ct);
}

public sealed record RenewedElsewhereResult(
    DateOnly? RegisterDate, int SalesChecked, int SalesMarked, decimal SalesMarkedAmount,
    int RenewalsChecked, int RenewalsMarked, decimal RenewalsMarkedAmount);

/// <summary>
/// A one-off cleanup of old records (run from POST /api/admin/ontraport-sales/renewed-elsewhere).
/// Before Renewtron could finish its renewals reliably, the ones it failed were renewed by hand at
/// ASIC, and nothing told Renewtron, so they sat in the work queues for good. ASIC cancels a name that
/// isn't renewed, almost always within 30–60 days of the due date (91% of 2026's cancellations).
/// So a name that is still on our copy of ASIC's public register (data.gov.au, imported nightly by
/// <see cref="BusinessNameImportService"/>) more than <see cref="Grace"/> after it was due has
/// been renewed. Only exact name matches under the same ABN count; anything less stays for a person.
/// </summary>
public class RenewedElsewhereService(ApplicationDbContext db, ILogger<RenewedElsewhereService> logger) : IRenewedElsewhereService
{
    /// <summary>How long after the due date a still-registered name counts as renewed.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(60);

    private sealed record Candidate(Guid Id, string Abn, string Name, DateTime Due, decimal Amount);
    private sealed record RegisterRow(string Abn, string Name, string RegistrationDate);

    public async Task<RenewedElsewhereResult> MarkAsync(bool dryRun, CancellationToken ct)
    {
        var import = await db.BusinessNameImports.AsNoTracking().FirstOrDefaultAsync(i => i.IsActive, ct);
        if (import is null)
        {
            logger.LogInformation("Renewed-elsewhere check: no business names register loaded yet");
            return new(null, 0, 0, 0, 0, 0, 0);
        }

        // The register is a snapshot: judge "still registered" as of its publication date.
        var registerAt = DateTime.TryParse(import.SourceModified, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var published)
            ? published
            : import.CompletedAt ?? import.StartedAt;
        var dueBefore = registerAt - Grace;
        var note = $"Still registered at ASIC on {registerAt:d MMM yyyy}, more than {Grace.Days} days after it was due: renewed outside Renewtron.";

        // Sales that never started a renewal (a renewal, when there is one, is checked below).
        var sales = await db.OntraportSales
            .Where(s => s.HandledAt == null && s.RenewalRequestId == null && s.RenewalDueDate != null && s.RenewalDueDate < dueBefore
                && (s.Status == OntraportSaleStatus.RenewalFailed || s.Status == OntraportSaleStatus.IneligibleForRenewal))
            .Select(s => new Candidate(s.Id, s.Abn, s.BusinessName, s.RenewalDueDate!.Value, s.AmountPaid))
            .ToListAsync(ct);

        // Failed or stuck renewals: due date from the client's name, else the sale that paid for it.
        var renewalRows = await db.RenewalRequests
            .Where(r => !r.IsTest && r.HandledAt == null
                && (r.Status == RenewalStatus.Failed || r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing))
            .Select(r => new
            {
                r.Id, Abn = r.SearchResult.SearchLog.Abn, Name = r.SearchResult.BusinessName, r.Amount,
                NameDue = db.ClientBusinessNames.Where(n => n.Id == r.ClientBusinessNameId).Select(n => n.RenewalDueDate).FirstOrDefault(),
                SaleDue = db.OntraportSales.Where(s => s.RenewalRequestId == r.Id).Select(s => s.RenewalDueDate).FirstOrDefault(),
                At = r.LastAttemptAt ?? r.InitiatedAt,
            })
            .ToListAsync(ct);
        var renewals = renewalRows
            // Untouched for the same grace too, so a renewal under way is never marked.
            .Where(r => r.At < dueBefore)
            .Select(r => new Candidate(r.Id, r.Abn, r.Name,
                r.NameDue is { } nd ? nd.ToDateTime(TimeOnly.MinValue) : r.SaleDue ?? DateTime.MaxValue, r.Amount))
            .Where(c => c.Due < dueBefore)
            .ToList();

        var registered = await RegisteredKeysAsync(import.Id, sales.Concat(renewals).Select(c => Helpers.NormalizeAbn(c.Abn)), ct);
        bool Renewed(Candidate c) => registered.Contains((Helpers.NormalizeAbn(c.Abn), BusinessNameText.MatchKey(c.Name)));

        var salesHit = sales.Where(Renewed).ToList();
        var renewalsHit = renewals.Where(Renewed).ToList();

        if (!dryRun && (salesHit.Count > 0 || renewalsHit.Count > 0))
        {
            var now = DateTime.UtcNow;
            foreach (var chunk in salesHit.Select(c => c.Id).Chunk(500))
                await db.OntraportSales.Where(s => chunk.Contains(s.Id) && s.HandledAt == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.HandledAt, now).SetProperty(s => s.HandledNote, note), ct);
            foreach (var chunk in renewalsHit.Select(c => c.Id).Chunk(500))
                await db.RenewalRequests.Where(r => chunk.Contains(r.Id) && r.HandledAt == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(r => r.HandledAt, now).SetProperty(r => r.HandledNote, note)
                        .SetProperty(r => r.NextRetryAt, (DateTime?)null), ct);
        }

        var result = new RenewedElsewhereResult(DateOnly.FromDateTime(registerAt),
            sales.Count, salesHit.Count, salesHit.Sum(c => c.Amount),
            renewals.Count, renewalsHit.Count, renewalsHit.Sum(c => c.Amount));
        logger.LogInformation("Renewed-elsewhere check{DryRun}: {Result}", dryRun ? " (dry run)" : "", result);
        return result;
    }

    /// <summary>(ABN, name match key) for every registered name under the given ABNs.</summary>
    private async Task<HashSet<(string, string)>> RegisteredKeysAsync(Guid importId, IEnumerable<string> abns, CancellationToken ct)
    {
        var keys = new HashSet<(string, string)>();
        foreach (var chunk in abns.Where(a => a.Length == 11).Distinct().Chunk(500))
        {
            // The ABN list is digits only (normalised and length-checked), so inlining it is safe,
            // and as varchar literals the index on Abn is used.
            var list = string.Join(",", chunk.Where(a => a.All(char.IsAsciiDigit)).Select(a => $"'{a}'"));
            if (list.Length == 0) continue;
#pragma warning disable EF1002 // digits-only literals, built above
            var rows = await db.Database.SqlQueryRaw<RegisterRow>(
                $"SELECT Abn, Name, RegistrationDate FROM RegisteredBusinessNames WITH (NOLOCK) WHERE ImportId = {{0}} AND Abn IN ({list})",
                importId).ToListAsync(ct);
#pragma warning restore EF1002
            foreach (var r in rows) keys.Add((r.Abn, BusinessNameText.MatchKey(r.Name)));
        }
        return keys;
    }
}
