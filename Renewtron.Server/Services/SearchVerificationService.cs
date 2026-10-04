using Asic.Client.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// Confirms a <see cref="SearchSource.Local"/> search against ASIC: fills in ASIC account
/// numbers, adds names ASIC lists that our copy hasn't caught up with, and marks names ASIC
/// won't renew as unavailable. Payment is refused until this has run.
/// </summary>
public interface ISearchVerificationService
{
    /// <summary>True once the search is settled (confirmed, or ASIC said no); false when ASIC couldn't be reached.</summary>
    Task<bool> VerifyAsync(Guid searchLogId);

    /// <summary>Hangfire entry point: throws while ASIC is unreachable so the job is retried.</summary>
    [AutomaticRetry(Attempts = 10, DelaysInSeconds = [60, 120, 300, 600, 900, 1800, 1800, 3600, 3600, 3600])]
    Task VerifyOrRetryAsync(Guid searchLogId);
}

public class SearchVerificationService(
    ApplicationDbContext db,
    IAsicSearchCoordinator asic,
    ILeadEmailService leadEmail,
    IBackgroundJobClient jobs,
    ILogger<SearchVerificationService> logger) : ISearchVerificationService
{
    /// <summary>Starts the check now, outside the request, plus a durable retry in case this process dies or ASIC is down.</summary>
    public static void Start(IServiceScopeFactory scopes, IBackgroundJobClient jobs, Guid searchLogId)
    {
        jobs.Schedule<ISearchVerificationService>(s => s.VerifyOrRetryAsync(searchLogId), TimeSpan.FromSeconds(90));
        _ = Task.Run(async () =>
        {
            using var scope = scopes.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<ISearchVerificationService>().VerifyAsync(searchLogId); }
            catch (Exception ex)
            {
                scope.ServiceProvider.GetRequiredService<ILogger<SearchVerificationService>>()
                    .LogError(ex, "Verifying search {SearchLogId} failed; the scheduled retry will pick it up", searchLogId);
            }
        });
    }

    public async Task VerifyOrRetryAsync(Guid searchLogId)
    {
        if (!await VerifyAsync(searchLogId))
            throw new InvalidOperationException($"ASIC could not be reached to verify search {searchLogId}; retrying.");
    }

    public async Task<bool> VerifyAsync(Guid searchLogId)
    {
        var log = await db.SearchLogs.Include(s => s.Results).FirstOrDefaultAsync(s => s.Id == searchLogId);
        if (log is null || log.IsVerified) return true;

        var result = await asic.SearchAsync(log.Abn);

        // The immediate run and the scheduled retry can await the same ASIC search and finish
        // together; serialise the write so only the first applies it.
        await ApplyLock.WaitAsync();
        try
        {
            await db.Entry(log).ReloadAsync();
            if (log.IsVerified) return true;
            return await ApplyAsync(log, result);
        }
        finally
        {
            ApplyLock.Release();
        }
    }

    private static readonly SemaphoreSlim ApplyLock = new(1);

    private async Task<bool> ApplyAsync(SearchLog log, BusinessNamesResult result)
    {

        if (AsicSearchCoordinator.IsTransient(result))
        {
            log.VerificationError = Truncate(result.ErrorMessage, 500);
            await db.SaveChangesAsync();
            logger.LogWarning("ASIC unreachable verifying search {SearchLogId} for {Abn}: {Error}", log.Id, log.Abn, result.ErrorMessage);
            return false;
        }

        var leads = await db.Leads.Where(l => l.SearchLogId == log.Id).ToListAsync();

        if (result.Success && result.BusinessNames.Count > 0)
        {
            ApplyAsicNames(log, result.BusinessNames);
            log.VerifiedAt = DateTime.UtcNow;
            log.VerificationError = null;
            await db.SaveChangesAsync();
            logger.LogInformation("Search {SearchLogId} for {Abn} confirmed by ASIC: {Available} of {Total} name(s) renewable",
                log.Id, log.Abn, log.Results.Count(r => r.IsAvailable), log.Results.Count);

            // The names are now ASIC's — send them to the leads' Ontraport contacts.
            foreach (var lead in leads)
                jobs.Enqueue<IOntraportContactPushService>(s => s.PushLeadAsync(lead.Id));
            return true;
        }

        // ASIC's answer is no: not due, already in progress, or no names on its renewal list.
        foreach (var r in log.Results) r.IsAvailable = false;
        log.VerifiedAt = DateTime.UtcNow;
        log.VerificationError = Truncate(result.ErrorMessage ?? "No business names found", 500);
        log.ResultsCount = 0;

        var message = result.ErrorMessage ?? "";
        var outcome = LeadOutcomes.FromAsicError(message);
        foreach (var lead in leads.Where(l => !l.ConvertedToRenewal))
        {
            lead.Outcome = outcome;
            lead.OutcomeMessage = message;
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Search {SearchLogId} for {Abn}: ASIC says {Outcome} ({Message})", log.Id, log.Abn, outcome, message);

        foreach (var lead in leads.Where(l => !l.ConvertedToRenewal))
            await LeadOutcomes.SendEmailAsync(leadEmail, lead, outcome, logger);
        return true;
    }

    private void ApplyAsicNames(SearchLog log, List<BusinessName> asicNames)
    {
        var byKey = asicNames
            .GroupBy(n => BusinessNameText.MatchKey(n.Name))
            .ToDictionary(g => g.Key, g => new Queue<BusinessName>(g));

        foreach (var r in log.Results)
        {
            if (byKey.TryGetValue(BusinessNameText.MatchKey(r.BusinessName), out var matches) && matches.Count > 0)
            {
                var n = matches.Dequeue();
                r.BusinessName = Truncate(n.Name, 200)!;
                r.AccountNumber = n.AccountNumber;
                r.RegistrationDate = n.RegistrationDate;
                r.IsAvailable = true;
            }
            else
            {
                r.IsAvailable = false;
            }
        }

        // Names ASIC will renew that our copy didn't have (registered since the last import).
        foreach (var n in byKey.Values.SelectMany(q => q))
        {
            var added = new SearchResult
            {
                Id = Guid.NewGuid(),
                SearchLogId = log.Id,
                BusinessName = Truncate(n.Name, 200)!,
                AccountNumber = n.AccountNumber,
                RegistrationDate = n.RegistrationDate,
                IsAvailable = true,
            };
            db.SearchResults.Add(added);
            log.Results.Add(added);
        }
        log.ResultsCount = log.Results.Count(r => r.IsAvailable);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

/// <summary>How ASIC's renewal search failures map to a lead outcome, and the email each one gets.</summary>
public static class LeadOutcomes
{
    public static LeadOutcome FromAsicError(string? message)
    {
        message ??= "";
        if (message.Contains("already in progress", StringComparison.OrdinalIgnoreCase)) return LeadOutcome.RenewalInProgress;
        if (message.Contains("not due for renewal", StringComparison.OrdinalIgnoreCase)) return LeadOutcome.NotDueForRenewal;
        return LeadOutcome.NoBusinessNames;
    }

    public static async Task SendEmailAsync(ILeadEmailService leadEmail, Lead lead, LeadOutcome outcome, ILogger logger)
    {
        try
        {
            switch (outcome)
            {
                case LeadOutcome.NotDueForRenewal: await leadEmail.SendNotDueForRenewalEmailAsync(lead); break;
                case LeadOutcome.RenewalInProgress: await leadEmail.SendRenewalInProgressEmailAsync(lead); break;
                case LeadOutcome.NoBusinessNames: await leadEmail.SendNoBusinessNamesEmailAsync(lead); break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send the {Outcome} email for lead {LeadId}", outcome, lead.Id);
        }
    }
}
