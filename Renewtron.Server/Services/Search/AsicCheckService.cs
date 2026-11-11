using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Modules;
using Renewtron.Services.AsicRegistry;
using Asic.Integrations.Renewal;

namespace Renewtron.Services;

public interface IAsicCheckService
{
    /// <summary>
    /// Checks every unfinished paid renewal against ASIC (the register file, then ASIC's renewal
    /// page for the ones the register can't settle) and records what it finds on the order.
    /// With <paramref name="dryRun"/> it only reports.
    /// </summary>
    Task<AsicCheckReport> CheckAllAsync(bool dryRun, CancellationToken ct);

    /// <summary>Checks one order now, asking ASIC's renewal page whatever the register says.</summary>
    Task<AsicCheckReport.Item?> CheckOneAsync(Guid orderId, CancellationToken ct);
}

public sealed record AsicCheckReport(
    DateOnly? RegisterDate, int Checked, int AskedAsic, int RenewedElsewhere, int Lapsed, int NameMismatch, int NotOfferedNow,
    int StillDue, IReadOnlyList<AsicCheckReport.Item> Items)
{
    public sealed record Item(Guid OrderId, string BusinessName, string? Outcome, string Evidence);
}

/// <summary>
/// Renewtron's own ASIC check, so no one has to look names up by hand or in another tool. Runs
/// nightly after the register import, and from the order page. Only outcomes it set itself are
/// ever replaced; a person's decision stands.
/// </summary>
public class AsicCheckService(
    ApplicationDbContext db,
    IAsicRenewalClient asic,
    IRenewalOrderService orders,
    IAsicDueDateService dueDates,
    ILogger<AsicCheckService> logger) : IAsicCheckService
{
    public const string RecurringJobId = "asic-check";
    public const string By = "ASIC check";

    /// <summary>Renewal-page lookups per nightly run: each is a real ASIC session, so keep it modest.</summary>
    public const int MaxAsicLookups = 60;
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(7);

    /// <summary>Outcomes the check may set or clear; anything a person decided is left alone.</summary>
    private static readonly RenewalOrderReason[] Replaceable =
        [RenewalOrderReason.NotOfferedNow, RenewalOrderReason.NameMismatch];

    private sealed record RegisterRow(string Abn, string Name);

    public async Task<AsicCheckReport> CheckAllAsync(bool dryRun, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var candidates = await Candidates().ToListAsync(ct);
        var (registerDate, register) = await RegisterAsync(candidates.Select(o => o.Abn), ct);
        var asicDue = await AsicDueByNameAsync(candidates.Select(o => o.ClientBusinessNameId), ct);

        var items = new List<AsicCheckReport.Item>();
        int asked = 0, stillDue = 0;
        foreach (var order in candidates)
        {
            if (order.DueDate is not { } due) continue;
            var names = register.TryGetValue(order.Abn, out var n) ? n : [];
            var finding = registerDate is { } rd
                ? AsicCheckRules.FromRegister(order.BusinessName, due, rd, names, AsicDueOf(asicDue, order))
                : null;

            if (finding is null && asked < MaxAsicLookups && NeedsAsking(order, today, now))
            {
                asked++;
                finding = await AskAsicAsync(order, due, today, registerDate is null ? null : names, ct);
                if (!dryRun) order.AsicCheckedAt = now;
            }
            if (finding is null) continue;
            if (finding.StillDue) stillDue++;
            if (!dryRun) await ApplyAsync(order, finding, registerDate, now, ct);
            items.Add(new(order.Id, order.BusinessName, finding.Outcome?.ToString(), finding.Evidence));
        }

        if (!dryRun)
        {
            await db.SaveChangesAsync(ct);
            await orders.RefreshAsync(ct);
        }
        var report = new AsicCheckReport(registerDate, candidates.Count, asked,
            Count(items, RenewalOrderReason.RenewedElsewhere), Count(items, RenewalOrderReason.Lapsed),
            Count(items, RenewalOrderReason.NameMismatch), Count(items, RenewalOrderReason.NotOfferedNow), stillDue, items);
        logger.LogInformation("ASIC check{DryRun}: {Checked} orders, {Asked} asked at ASIC, {Elsewhere} renewed elsewhere, {Lapsed} lapsed, {Mismatch} name mismatch, {NotOffered} not offered now, {StillDue} still due",
            dryRun ? " (dry run)" : "", report.Checked, asked, report.RenewedElsewhere, report.Lapsed, report.NameMismatch, report.NotOfferedNow, stillDue);
        return report;
    }

    public async Task<AsicCheckReport.Item?> CheckOneAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.RenewalOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order?.DueDate is not { } due) return null;
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var (registerDate, register) = await RegisterAsync([order.Abn], ct);
        var names = register.TryGetValue(order.Abn, out var n) ? n : [];
        var asicDue = await AsicDueByNameAsync([order.ClientBusinessNameId], ct);

        // A person asked: ask ASIC itself, and fall back on the register only when ASIC doesn't answer.
        var finding = await AskAsicAsync(order, due, today, registerDate is null ? null : names, ct)
            ?? (registerDate is { } rd ? AsicCheckRules.FromRegister(order.BusinessName, due, rd, names, AsicDueOf(asicDue, order)) : null);
        order.AsicCheckedAt = now;
        if (finding is not null && CanSet(order)) await ApplyAsync(order, finding, registerDate, now, ct);
        await db.SaveChangesAsync(ct);
        await orders.RefreshQuietlyAsync(ct);
        return new(order.Id, order.BusinessName, finding?.Outcome?.ToString(),
            finding?.Evidence ?? "ASIC didn't answer. Try again later.");
    }

    /// <summary>Unfinished orders with a due date whose outcome, if any, the check set itself.</summary>
    private IQueryable<RenewalOrder> Candidates() => db.RenewalOrders
        .Where(o => o.DueDate != null && o.Abn.Length == 11
            && (o.Stage == RenewalOrderStage.NeedsPerson || o.Stage == RenewalOrderStage.Due || o.Stage == RenewalOrderStage.Waiting))
        .Where(o => o.Outcome == null || (o.OutcomeBy == By && Replaceable.Contains(o.Outcome.Value)));

    private static bool CanSet(RenewalOrder o) =>
        o.Stage is not (RenewalOrderStage.Renewed or RenewalOrderStage.Renewing)
        && (o.Outcome is null || (o.OutcomeBy == By && Replaceable.Contains(o.Outcome.Value)));

    /// <summary>
    /// Only ask ASIC about an order that is stuck or overdue, at most weekly: a name in its window
    /// that is renewing normally needs no lookup.
    /// </summary>
    private static bool NeedsAsking(RenewalOrder o, DateOnly today, DateTime now)
    {
        if (o.AsicCheckedAt is { } at && now - at < AskAgainAfter) return false;
        return o.Stage == RenewalOrderStage.NeedsPerson
            || o.Outcome == RenewalOrderReason.NotOfferedNow
            || (o.Stage == RenewalOrderStage.Due && o.DueDate < today);
    }

    private async Task<AsicCheckRules.Finding?> AskAsicAsync(RenewalOrder order, DateOnly due, DateOnly today, IReadOnlyList<string>? registerNames, CancellationToken ct)
    {
        try
        {
            var result = await asic.SearchByAbnAsync(order.Abn);
            var offered = result.BusinessNames?.Select(b => b.Name).ToList() ?? [];
            bool? onRegister = registerNames is null ? null : BusinessNameText.FindName(registerNames, x => x, order.BusinessName) is not null;
            return AsicCheckRules.FromRenewalPage(order.BusinessName, due, today, result.Success && offered.Count > 0, offered,
                result.ErrorMessage, onRegister, registerNames ?? []);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "ASIC check: renewal page lookup failed for order {OrderId}", order.Id);
            return null;
        }
    }

    private async Task ApplyAsync(RenewalOrder order, AsicCheckRules.Finding finding, DateOnly? registerDate, DateTime now, CancellationToken ct)
    {
        if (finding.StillDue)
        {
            // ASIC offers it: drop an earlier "not offered" or "wrong name" so the pipeline carries on.
            if (order.Outcome is { } o && order.OutcomeBy == By && Replaceable.Contains(o)) ClearOutcome(order);
            return;
        }
        if (order.Outcome == finding.Outcome && order.Evidence == finding.Evidence) return;
        order.Outcome = finding.Outcome;
        order.OutcomeAt = now;
        order.OutcomeBy = By;
        order.Evidence = Trim(finding.Evidence, 1000);
        order.EvidenceSource = finding.Source;
        order.EvidenceAt = finding.Source == OrderEvidenceSource.AsicRegister && registerDate is { } rd
            ? rd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : now;

        if (order.ClientBusinessNameId is not { } nameId || db.ClientBusinessNames.Find(nameId) is not { } name) return;
        if (finding.Outcome == RenewalOrderReason.RenewedElsewhere && order.DueDate is { } due
            && (name.RenewalDueDate is null || name.RenewalDueDate <= due))
        {
            // The new date is on ASIC's register search: read it there (1 or 3 years on).
            if (await dueDates.LookupAsync(order.Abn, ct) is { } found)
                await dueDates.ApplyAsync(order.Abn, found, now, ct);

            // ASIC couldn't be read, or hasn't caught up yet: a renewal is at least a year, so
            // this is the earliest it can be. The nightly refresh replaces it with ASIC's date.
            if (name.RenewalDueDate is null || name.RenewalDueDate <= due)
            {
                name.RenewalDueDate = due.AddYears(1);
                name.DueDateSource = RenewalDueDateSource.Estimated;
                name.UpdatedAt = now;
            }
        }
        else if (finding.Outcome == RenewalOrderReason.Lapsed && finding.Source == OrderEvidenceSource.AsicRegister)
        {
            name.Status = ClientBusinessNameStatus.Cancelled;
            name.UpdatedAt = now;
        }
    }

    /// <summary>ASIC's own due date for each of these names, where its register search has shown one.</summary>
    private async Task<Dictionary<Guid, DateOnly>> AsicDueByNameAsync(IEnumerable<Guid?> nameIds, CancellationToken ct)
    {
        var ids = nameIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.ClientBusinessNames.AsNoTracking()
            .Where(n => ids.Contains(n.Id) && n.DueDateSource == RenewalDueDateSource.AsicRegister && n.RenewalDueDate != null)
            .ToDictionaryAsync(n => n.Id, n => n.RenewalDueDate!.Value, ct);
    }

    private static DateOnly? AsicDueOf(Dictionary<Guid, DateOnly> asicDue, RenewalOrder order) =>
        order.ClientBusinessNameId is { } id && asicDue.TryGetValue(id, out var d) ? d : null;

    public static void ClearOutcome(RenewalOrder order)
    {
        order.Outcome = null;
        order.OutcomeAt = null;
        order.OutcomeBy = null;
        order.Evidence = null;
        order.EvidenceSource = null;
        order.EvidenceAt = null;
        order.OutcomeReference = null;
    }

    /// <summary>The active register's publication date and its names under the given ABNs.</summary>
    private async Task<(DateOnly?, Dictionary<string, List<string>>)> RegisterAsync(IEnumerable<string> abns, CancellationToken ct)
    {
        var byAbn = new Dictionary<string, List<string>>();
        var import = await db.BusinessNameImports.AsNoTracking().FirstOrDefaultAsync(i => i.IsActive, ct);
        if (import is null) return (null, byAbn);
        var published = DateTime.TryParse(import.SourceModified, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var p) ? p : import.CompletedAt ?? import.StartedAt;

        foreach (var chunk in abns.Select(Helpers.NormalizeAbn).Where(a => a.Length == 11 && a.All(char.IsAsciiDigit)).Distinct().Chunk(500))
        {
            // Digits only (checked above), so inlining keeps the Abn index usable.
            var list = string.Join(",", chunk.Select(a => $"'{a}'"));
#pragma warning disable EF1002 // digits-only literals, built above
            var rows = await db.Database.SqlQueryRaw<RegisterRow>(
                $"SELECT Abn, Name FROM RegisteredBusinessNames WITH (NOLOCK) WHERE ImportId = {{0}} AND Abn IN ({list})",
                import.Id).ToListAsync(ct);
#pragma warning restore EF1002
            foreach (var r in rows)
            {
                if (!byAbn.TryGetValue(r.Abn, out var names)) byAbn[r.Abn] = names = [];
                names.Add(BusinessNameText.Clean(r.Name));
            }
        }
        return (DateOnly.FromDateTime(published), byAbn);
    }

    private static int Count(List<AsicCheckReport.Item> items, RenewalOrderReason reason) =>
        items.Count(i => i.Outcome == reason.ToString());

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}
