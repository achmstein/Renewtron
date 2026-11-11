using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Settings;
using Asic.Integrations.Registry;

namespace Renewtron.Services.AsicRegistry;

/// <summary>What ASIC's register search shows for one name.</summary>
public sealed record AsicNameDue(string Name, DateOnly? RenewalDate, string? Status);

public sealed record AsicDueDateReport(int Looked, int Updated, int Failed, bool Stopped);

/// <summary>
/// Business names' real renewal due dates, read from ASIC's register search, in place of
/// Renewtron's estimates (registration anniversaries, or an old due date plus a year).
/// </summary>
public interface IAsicDueDateService
{
    bool IsEnabled { get; }

    /// <summary>
    /// ASIC's renewal date for every name under <paramref name="abn"/>, keyed by
    /// <see cref="BusinessNameText.MatchKey"/>. Null when ASIC couldn't be asked (switched off,
    /// unreachable, CAPTCHA failed): callers then fall back on an estimate.
    /// </summary>
    Task<IReadOnlyDictionary<string, AsicNameDue>?> LookupAsync(string abn, CancellationToken ct);

    /// <summary>
    /// Records what ASIC said on the business names held under <paramref name="abn"/> (tracked
    /// entities; the caller saves). Returns how many due dates changed.
    /// </summary>
    Task<int> ApplyAsync(string abn, IReadOnlyDictionary<string, AsicNameDue> found, DateTime now, CancellationToken ct);

    /// <summary>Nightly: reads ASIC's date for names whose due date is unknown, estimated, stale or near.</summary>
    Task<AsicDueDateReport> RefreshAsync(CancellationToken ct);
}

public class AsicDueDateService(
    ApplicationDbContext db,
    IAsicRegistryClientFactory clients,
    IOptionsMonitor<AsicRegistrySettings> settings,
    ILogger<AsicDueDateService> logger) : IAsicDueDateService
{
    public const string RecurringJobId = "asic-due-dates";

    // Each lookup is a real ASIC session (and maybe a CAPTCHA): cap them across the whole app.
    private static readonly SemaphoreSlim Sessions = new(2);

    public bool IsEnabled => settings.CurrentValue.Enabled;

    public async Task<IReadOnlyDictionary<string, AsicNameDue>?> LookupAsync(string abn, CancellationToken ct)
    {
        if (!IsEnabled) return null;
        abn = Modules.Helpers.NormalizeAbn(abn);
        if (abn.Length != 11) return null;

        await Sessions.WaitAsync(ct);
        try
        {
            var client = clients.Create();
            try
            {
                var result = await client.SearchByAbnAsync(abn, ct);
                if (!result.Succeeded)
                {
                    logger.LogWarning("ASIC register search for {Abn} failed: {Reason}", abn, result.Reason);
                    return null;
                }
                var byKey = new Dictionary<string, AsicNameDue>();
                foreach (var bn in result.BusinessNames)
                {
                    var key = BusinessNameText.MatchKey(bn.Name);
                    if (key.Length == 0) continue;
                    byKey[key] = new AsicNameDue(BusinessNameText.Clean(bn.Name), AsicDates.Parse(bn.RenewalDate), bn.Status?.Trim());
                }
                return byKey;
            }
            finally
            {
                (client as IDisposable)?.Dispose();
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "ASIC register search for {Abn} failed", abn);
            return null;
        }
        finally
        {
            Sessions.Release();
        }
    }

    public async Task<int> ApplyAsync(string abn, IReadOnlyDictionary<string, AsicNameDue> found, DateTime now, CancellationToken ct)
    {
        abn = Modules.Helpers.NormalizeAbn(abn);
        var names = await db.ClientBusinessNames.Where(n => n.Abn == abn).ToListAsync(ct);
        var updated = 0;
        foreach (var name in names)
        {
            name.DueDateCheckedAt = now;
            if (!found.TryGetValue(BusinessNameText.MatchKey(name.Name), out var asic) || asic.RenewalDate is not { } due) continue;
            if (!ShouldTake(name.RenewalDueDate, name.DueDateSource, due)) continue;
            name.RenewalDueDate = due;
            name.DueDateSource = RenewalDueDateSource.AsicRegister;
            name.UpdatedAt = now;
            updated++;
        }
        return updated;
    }

    /// <summary>
    /// ASIC's date is the truth, except straight after a renewal we lodged: the register can lag a
    /// few days, so a later date worked out from our own renewal stands until ASIC catches up.
    /// </summary>
    public static bool ShouldTake(DateOnly? current, RenewalDueDateSource? source, DateOnly asic)
    {
        if (current == asic && source == RenewalDueDateSource.AsicRegister) return false;
        if (source == RenewalDueDateSource.Renewal && current > asic) return false;
        return true;
    }

    /// <summary>
    /// The due date Renewtron already holds for a name from ASIC or from a renewal it lodged:
    /// no network. Null when it only has an estimate (or nothing).
    /// </summary>
    public static async Task<DateOnly?> KnownDueDateAsync(ApplicationDbContext db, string abn, string? businessName, CancellationToken ct = default)
    {
        abn = Modules.Helpers.NormalizeAbn(abn);
        var key = BusinessNameText.MatchKey(businessName);
        if (key.Length == 0) return null;
        var names = await db.ClientBusinessNames.AsNoTracking()
            .Where(n => n.Abn == abn && n.RenewalDueDate != null
                && (n.DueDateSource == RenewalDueDateSource.AsicRegister || n.DueDateSource == RenewalDueDateSource.Renewal))
            .Select(n => new { n.Name, n.RenewalDueDate })
            .ToListAsync(ct);
        return names.FirstOrDefault(n => BusinessNameText.MatchKey(n.Name) == key)?.RenewalDueDate;
    }

    public async Task<AsicDueDateReport> RefreshAsync(CancellationToken ct)
    {
        var s = settings.CurrentValue;
        if (!s.Enabled) return new(0, 0, 0, false);

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, Sydney));
        var stale = now.AddDays(-s.RecheckAfterDays);
        var soon = today.AddDays(s.RecheckWhenDueWithinDays);

        // Never read, or read a while ago and either still not from ASIC or coming due (it may
        // have been renewed elsewhere). Soonest due first; unknown dates last.
        var candidates = await db.ClientBusinessNames.AsNoTracking()
            .Where(n => n.Status == ClientBusinessNameStatus.Active && n.Abn.Length == 11)
            .Where(n => n.DueDateCheckedAt == null
                || (n.DueDateCheckedAt < stale
                    && (n.DueDateSource != RenewalDueDateSource.AsicRegister || n.RenewalDueDate <= soon)))
            .OrderBy(n => n.RenewalDueDate == null ? 1 : 0).ThenBy(n => n.RenewalDueDate)
            .Select(n => n.Abn)
            .ToListAsync(ct);
        var abns = candidates.Distinct().Take(Math.Max(0, s.MaxLookupsPerRun)).ToList();

        int looked = 0, updated = 0, failed = 0, inARow = 0;
        var stopped = false;
        foreach (var abn in abns)
        {
            ct.ThrowIfCancellationRequested();
            looked++;
            var found = await LookupAsync(abn, ct);
            if (found is null)
            {
                failed++;
                if (++inARow >= Math.Max(1, s.ConsecutiveFailureLimit))
                {
                    stopped = true;
                    logger.LogWarning("ASIC due dates: stopped after {Failures} lookups failed in a row", inARow);
                    break;
                }
                continue;
            }
            inARow = 0;
            updated += await ApplyAsync(abn, found, now, ct);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("ASIC due dates: looked up {Looked} ABNs, {Updated} due dates updated, {Failed} lookups failed{Stopped}",
            looked, updated, failed, stopped ? " (stopped early)" : "");
        return new(looked, updated, failed, stopped);
    }

    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
}
