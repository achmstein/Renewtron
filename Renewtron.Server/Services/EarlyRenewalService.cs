using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// Renewals paid before ASIC will take them ("pay early"). ASIC only opens a business name's
/// renewal in the 29 days before it's due, so a paid-early renewal waits: it is kept as a
/// failed run in <see cref="RenewalErrorCategories.NotDueYet"/> with <see cref="RenewalRequest.NextRetryAt"/>
/// set to when to look again (the same shape every "ASIC says not due yet" run has), and this
/// service looks each morning. Once ASIC lists the name, its account number goes on the
/// search result and the renewal is lodged like any other.
/// </summary>
public interface IEarlyRenewalService
{
    /// <summary>Daily: asks ASIC about every waiting renewal whose check date has come, and starts the ones it now lists.</summary>
    Task<EarlyRenewalReport> CheckWaitingAsync();
}

public sealed record EarlyRenewalReport(int Checked, int Started, int StillWaiting, int NeedPerson);

public class EarlyRenewalService(
    ApplicationDbContext db,
    IAsicSearchCoordinator asic,
    IBackgroundJobClient jobs,
    ILogger<EarlyRenewalService> logger) : IEarlyRenewalService
{
    public const string RecurringJobId = "early-renewal-check";

    /// <summary>What a waiting renewal's run says, for the admin pages.</summary>
    public const string WaitingStep = "Waiting for ASIC";

    /* ───────────── Dates (pure, unit tested) ───────────── */

    /// <summary>
    /// The name's next due date as far as we can tell: the next anniversary of its registration
    /// on or after <paramref name="today"/>. A name renewed for 3 years is really due later;
    /// ASIC saying "not due" then simply moves the check on to the following anniversary.
    /// Null when the registration date can't be read.
    /// </summary>
    public static DateOnly? EstimatedDueDate(DateOnly today, string? registrationDate)
    {
        if (!TryParseDate(registrationDate, out var reg)) return null;
        var years = Math.Max(1, today.Year - reg.Year);
        var due = reg.AddYears(years);   // 29 Feb falls on 28 Feb in other years
        return due >= today ? due : reg.AddYears(years + 1);
    }

    /// <summary>
    /// When to ask ASIC next about a renewal it said isn't due: the day its renewal window
    /// should open (29 days before the estimated due date), or tomorrow when we're already
    /// inside that window. With no registration date, in a week.
    /// </summary>
    public static DateOnly NextCheck(DateOnly today, string? registrationDate)
    {
        if (EstimatedDueDate(today, registrationDate) is not { } due) return today.AddDays(7);
        var opens = due.AddDays(-RenewalOrderProjection.WindowDays);
        return opens > today ? opens : today.AddDays(1);
    }

    /// <summary>The check date as the moment the morning job will pick it up (UTC).</summary>
    public static DateTime CheckAt(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>Puts a renewal into the waiting state: paid, not lodged, looked at again on <paramref name="checkOn"/>.</summary>
    public static void MarkWaiting(RenewalRequest renewal, DateOnly checkOn)
    {
        renewal.Status = RenewalStatus.Failed;
        renewal.ErrorCategory = RenewalErrorCategories.NotDueYet;
        renewal.FailedAtStep = WaitingStep;
        renewal.ErrorMessage = $"Paid early: ASIC isn't taking this renewal yet. Renewtron asks ASIC again on {checkOn:dd/MM/yyyy} and lodges it once ASIC lists the name.";
        renewal.NextRetryAt = CheckAt(checkOn);
    }

    /// <summary>A renewal paid early that is waiting for ASIC (not a person's problem).</summary>
    public static bool IsWaiting(RenewalStatus status, string? category, DateTime? nextRetryAt) =>
        status == RenewalStatus.Failed && category == RenewalErrorCategories.NotDueYet && nextRetryAt != null;

    private static bool TryParseDate(string? s, out DateOnly date) =>
        DateOnly.TryParseExact((s ?? "").Trim(), ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"],
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);

    /* ───────────── The morning check ───────────── */

    public async Task<EarlyRenewalReport> CheckWaitingAsync()
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, Sydney));

        // Wizard renewals only: Ontraport sales have their own daily loop.
        var waiting = await db.RenewalRequests
            .Include(r => r.SearchResult).ThenInclude(sr => sr.SearchLog)
            // Test customers too: the search is read only, and their run never reaches ASIC.
            .Where(r => r.Source == RenewalSource.Renewtron && r.HandledAt == null
                        && r.Status == RenewalStatus.Failed
                        && r.ErrorCategory == RenewalErrorCategories.NotDueYet
                        && r.NextRetryAt != null && r.NextRetryAt <= now)
            .ToListAsync();

        int started = 0, stillWaiting = 0, needPerson = 0;
        foreach (var byAbn in waiting.GroupBy(r => r.SearchResult.SearchLog.Abn))
        {
            var result = await asic.SearchAsync(byAbn.Key);
            foreach (var renewal in byAbn)
            {
                var sr = renewal.SearchResult;
                if (AsicSearchCoordinator.IsTransient(result))
                {
                    renewal.NextRetryAt = CheckAt(today.AddDays(1));
                    stillWaiting++;
                    continue;
                }

                var listed = result.Success
                    ? result.BusinessNames.FirstOrDefault(n => BusinessNameText.MatchKey(n.Name) == BusinessNameText.MatchKey(sr.BusinessName))
                    : null;
                if (listed is not null)
                {
                    // ASIC now takes it: the account number it needs comes from this search.
                    sr.AccountNumber = listed.AccountNumber;
                    if (!string.IsNullOrWhiteSpace(listed.RegistrationDate)) sr.RegistrationDate = listed.RegistrationDate;
                    renewal.Status = RenewalStatus.Pending;
                    renewal.ErrorCategory = null;
                    renewal.ErrorMessage = null;
                    renewal.FailedAtStep = null;
                    renewal.NextRetryAt = null;
                    started++;
                    continue;
                }

                var message = result.ErrorMessage ?? "";
                var noNames = result.Success
                    ? false // other names on the ABN are due, this one isn't yet
                    : LeadOutcomes.FromAsicError(message) == LeadOutcome.NoBusinessNames;
                if (noNames)
                {
                    // ASIC has no names to renew under this ABN at all: cancelled or moved. A person decides.
                    renewal.ErrorCategory = RenewalErrorCategories.Terminal;
                    renewal.FailedAtStep = WaitingStep;
                    renewal.ErrorMessage = $"Paid early, but ASIC now lists no business names to renew under ABN {byAbn.Key}: {message}";
                    renewal.NextRetryAt = null;
                    needPerson++;
                    continue;
                }

                // Not due yet, or a renewal already in progress: look again later.
                var next = LeadOutcomes.FromAsicError(message) == LeadOutcome.RenewalInProgress
                    ? today.AddDays(1)
                    : NextCheck(today, sr.RegistrationDate);
                MarkWaiting(renewal, next);
                stillWaiting++;
            }
        }
        await db.SaveChangesAsync();

        foreach (var renewal in waiting.Where(r => r.Status == RenewalStatus.Pending))
        {
            var id = renewal.Id;
            jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(id));
        }
        if (waiting.Count > 0)
        {
            jobs.Enqueue<IRenewalOrderService>(s => s.RefreshAsync(CancellationToken.None));
            logger.LogInformation("Paid-early renewals: checked {Checked}, started {Started}, still waiting {Waiting}, need a person {NeedPerson}",
                waiting.Count, started, stillWaiting, needPerson);
        }
        return new EarlyRenewalReport(waiting.Count, started, stillWaiting, needPerson);
    }

    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
}
