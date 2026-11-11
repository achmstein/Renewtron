using Asic.Integrations.Renewal;
using Hangfire;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>The details step of the renewal wizard.</summary>
public record CreateLeadRequest(
    string Abn,
    string FullName,
    string Email,
    string? MobileNumber,
    DateOnly DateOfBirth,
    string? Tfn,
    Guid? SearchLogId,
    string? Source,
    string? OntraportContactId,
    string? VisitorId);

/// <summary>Where the request came from (stored on the lead, for fraud checks and support).</summary>
public record RequestOrigin(string? IpAddress, string? UserAgent, string? SessionId);

/// <summary>A captured lead, or why the details were refused.</summary>
public record LeadCaptureResult(Guid? LeadId, string? Error);

/// <summary>A name the customer may pay for, as the wizard lists it.</summary>
public record LeadNameView(Guid Id, string BusinessName, string AccountNumber, string RegistrationDate);

/// <summary>What the check step shows: the names to pay for, or why there are none.</summary>
public record LeadCheckResult(LeadOutcome Outcome, string? Message, bool Verified, IReadOnlyList<LeadNameView> BusinessNames);

/// <summary>
/// The renewal wizard's lead steps: taking the customer's details, then finding the names they
/// can renew. Kept out of the endpoints so the rules (test customers, pay early, local copy
/// first then ASIC) can be tested.
/// </summary>
public sealed class LeadWizard(
    ApplicationDbContext db,
    ILeadService leadService,
    ILeadEmailService leadEmail,
    IAsicSearchCoordinator asic,
    IBusinessNameFallbackService localNames,
    IOptionsSnapshot<AsicSettings> asicSettings,
    IOptionsSnapshot<TestModeSettings> testMode,
    IServiceScopeFactory scopes,
    IBackgroundJobClient jobs,
    ILogger<LeadWizard> logger)
{
    public async Task<LeadCaptureResult> CaptureAsync(CreateLeadRequest request, RequestOrigin origin)
    {
        if (!Modules.Helpers.IsValidAbn(request.Abn)) return new(null, "ABN must be 11 digits.");
        if (string.IsNullOrWhiteSpace(request.Email)) return new(null, "Email is required.");
        if (string.IsNullOrWhiteSpace(request.FullName)) return new(null, "Full name is required.");

        var lead = await leadService.CreateLeadAsync(new CreateLeadDto
        {
            Abn = Modules.Helpers.NormalizeAbn(request.Abn),
            FullName = request.FullName,
            Email = request.Email,
            MobileNumber = request.MobileNumber ?? string.Empty,
            DateOfBirth = request.DateOfBirth,
            Tfn = NormalizeTfn(request.Tfn),
            IpAddress = origin.IpAddress,
            UserAgent = origin.UserAgent,
            SessionId = origin.SessionId,
            Source = Clip(request.Source, 64),
            OntraportContactId = Clip(request.OntraportContactId, 20),
            VisitorId = Clip(request.VisitorId, 64),
            IsTest = testMode.Value.IsTestEmail(request.Email),
        });

        if (request.SearchLogId is { } sid)
            await leadService.LinkSearchLogAsync(lead.Id, sid);

        try { await leadEmail.SendLeadCapturedEmailAsync(lead); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send the lead-captured email for {LeadId}", lead.Id); }
        jobs.Enqueue<IOntraportContactPushService>(s => s.PushLeadAsync(lead.Id));

        return new(lead.Id, null);
    }

    /// <summary>
    /// Finds the names the lead can renew and records the search. Fastest path that is still
    /// ASIC's answer: the search the ABN step started is done. Otherwise list the names from our
    /// copy of the register straight away and confirm them with ASIC in the background; payment
    /// waits for that. Only an ABN our copy doesn't know waits on ASIC here. Null = no such lead.
    /// </summary>
    public async Task<LeadCheckResult?> CheckNamesAsync(Guid leadId)
    {
        var lead = await leadService.GetLeadAsync(leadId);
        if (lead is null) return null;

        var abn = Modules.Helpers.NormalizeAbn(lead.Abn);
        var found = await FindNamesAsync(lead, abn);

        if (!found.Result.Success || found.Result.BusinessNames.Count == 0)
            return await RecordNoNamesAsync(lead, abn, found);

        var searchLog = new SearchLog
        {
            Id = Guid.NewGuid(),
            Abn = abn,
            SearchedAt = DateTime.UtcNow,
            IpAddress = lead.IpAddress,
            UserAgent = lead.UserAgent,
            SessionId = lead.SessionId,
            Success = true,
            InitiatedBy = SearchInitiator.Customer,
            Source = found.Source,
            ResultsCount = found.Result.BusinessNames.Count,
            // ASIC has already answered for a test or paid-early search: nothing left to confirm.
            VerifiedAt = found.IsTestCustomer || found.PayEarly ? DateTime.UtcNow : null,
            VerificationError = found.AsicNotDueMessage is { } m ? m[..Math.Min(m.Length, 500)] : null,
        };
        var saved = found.Result.BusinessNames.Select(b => new SearchResult
        {
            Id = Guid.NewGuid(),
            SearchLogId = searchLog.Id,
            BusinessName = b.Name,
            AccountNumber = b.AccountNumber ?? "",
            RegistrationDate = b.RegistrationDate,
            PayEarly = found.PayEarly || (found.TestEarly?.Contains(BusinessNameText.MatchKey(b.Name)) ?? false),
        }).ToList();
        searchLog.Results = saved;
        db.SearchLogs.Add(searchLog);
        await db.SaveChangesAsync();
        await leadService.LinkSearchLogAsync(lead.Id, searchLog.Id);
        await leadService.UpdateLeadOutcomeAsync(lead.Id, LeadOutcome.RenewalAvailable, null);

        if (found.IsTestCustomer)
        {
            // Test customer: settled already, and never pushed to Ontraport.
        }
        else if (found.Source == SearchSource.Asic || found.PayEarly)
            // The business names are ASIC's: send them to the lead's Ontraport contact.
            jobs.Enqueue<IOntraportContactPushService>(s => s.PushLeadAsync(lead.Id));
        else if (!asicSettings.Value.ForceFallback)
            // Pushes to Ontraport once ASIC has confirmed the names.
            SearchVerificationService.Start(scopes, jobs, searchLog.Id);

        return new LeadCheckResult(LeadOutcome.RenewalAvailable, null, searchLog.IsVerified,
            saved.Select(r => new LeadNameView(r.Id, r.BusinessName, r.AccountNumber, r.RegistrationDate)).ToList());
    }

    private sealed record FoundNames(
        SearchSource Source, BusinessNamesResult Result, bool IsTestCustomer, HashSet<string>? TestEarly,
        bool PayEarly, string? AsicNotDueMessage);

    private async Task<FoundNames> FindNamesAsync(Lead lead, string abn)
    {
        var settings = asicSettings.Value;

        // A test customer can pick any name on the ABN, due or not: nothing is sent to ASIC,
        // so our copy of the register is the whole answer (ASIC only for an ABN it lacks).
        BusinessNamesResult? testNames = null;
        if (lead.IsTest && await localNames.SearchByAbnAsync(abn) is { Success: true, BusinessNames.Count: > 0 } local0)
            testNames = local0;

        if (testNames is not null)
            return new FoundNames(SearchSource.Local, testNames, true, await TestEarlyNamesAsync(abn, testNames), false, null);

        SearchSource source;
        BusinessNamesResult result;
        if (!settings.ForceFallback && asic.TryGetRecent(abn, out var recent))
        {
            source = SearchSource.Asic;
            result = recent;
        }
        else
        {
            if (!settings.ForceFallback) asic.Prefetch(abn);
            var local = await localNames.SearchByAbnAsync(abn);
            if ((local.Success && local.BusinessNames.Count > 0) || settings.ForceFallback)
            {
                source = SearchSource.Local;
                result = local;
            }
            else
            {
                source = SearchSource.Asic;
                result = await asic.SearchAsync(abn);
            }
        }

        // ASIC says not due, but the customer may pay early: list the names from our copy of
        // the register as payable and let the renewal wait for ASIC (EarlyRenewalService).
        // The wizard carries on as for any name: nothing tells the customer it isn't due.
        if (settings.AllowPayEarly && !result.Success
            && LeadOutcomes.FromAsicError(result.ErrorMessage) == LeadOutcome.NotDueForRenewal
            && await localNames.SearchByAbnAsync(abn) is { Success: true, BusinessNames.Count: > 0 } earlyNames)
        {
            logger.LogInformation("ABN {Abn}: ASIC says not due ({Message}); offering {Count} name(s) to pay early",
                abn, result.ErrorMessage, earlyNames.BusinessNames.Count);
            return new FoundNames(source, earlyNames, false, null, true, result.ErrorMessage);
        }

        return new FoundNames(source, result, false, null, false, null);
    }

    /// <summary>
    /// A test customer still asks ASIC (read only) which names it isn't taking yet, so the
    /// pay-early path can be tried end to end with Stripe's test keys. Their renewals never
    /// reach ASIC's payment step.
    /// </summary>
    private async Task<HashSet<string>?> TestEarlyNamesAsync(string abn, BusinessNamesResult testNames)
    {
        var settings = asicSettings.Value;
        if (!settings.AllowPayEarly || settings.ForceFallback) return null;

        var answer = asic.TryGetRecent(abn, out var r0) ? r0 : await asic.SearchAsync(abn);
        if (AsicSearchCoordinator.IsTransient(answer)) return null;
        if (!answer.Success && LeadOutcomes.FromAsicError(answer.ErrorMessage) != LeadOutcome.NotDueForRenewal) return null;

        var listed = answer.Success ? answer.BusinessNames.Select(n => BusinessNameText.MatchKey(n.Name)).ToHashSet() : [];
        return testNames.BusinessNames.Select(n => BusinessNameText.MatchKey(n.Name)).Where(k => !listed.Contains(k)).ToHashSet();
    }

    private async Task<LeadCheckResult> RecordNoNamesAsync(Lead lead, string abn, FoundNames found)
    {
        var result = found.Result;
        var failedLog = new SearchLog
        {
            Id = Guid.NewGuid(),
            Abn = abn,
            SearchedAt = DateTime.UtcNow,
            IpAddress = lead.IpAddress,
            UserAgent = lead.UserAgent,
            SessionId = lead.SessionId,
            Success = result.Success,
            InitiatedBy = SearchInitiator.Customer,
            Source = found.Source,
            ErrorMessage = result.ErrorMessage ?? (result.Success ? "No business names found" : "Search failed"),
            ResultsCount = 0,
        };
        db.SearchLogs.Add(failedLog);
        await db.SaveChangesAsync();
        await leadService.LinkSearchLogAsync(lead.Id, failedLog.Id);

        var message = result.ErrorMessage ?? "";
        var outcome = LeadOutcomes.FromAsicError(message);
        await leadService.UpdateLeadOutcomeAsync(lead.Id, outcome, message);
        if (await leadService.GetLeadAsync(lead.Id) is { } updated)
            await LeadOutcomes.SendEmailAsync(leadEmail, updated, outcome, logger);

        return new LeadCheckResult(outcome, message, true, []);
    }

    private static string? NormalizeTfn(string? tfn)
    {
        if (string.IsNullOrWhiteSpace(tfn)) return null;
        var digits = new string(tfn.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? null : digits;
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
