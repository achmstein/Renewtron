using Ato.Api.Contracts;
using Google.Protobuf.WellKnownTypes;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

public class AtoOnboardingService : IAtoOnboardingService
{
    private const int MaxPollAttempts = 240;        // ~4 hours at 60s/attempt
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly ApplicationDbContext _db;
    private readonly AtoApi.AtoApiClient _ato;
    private readonly IBackgroundJobClient _jobs;
    private readonly IOptionsMonitor<AtoAgentSettings> _atoAgentSettings;
    private readonly ILogger<AtoOnboardingService> _logger;

    public AtoOnboardingService(
        ApplicationDbContext db,
        AtoApi.AtoApiClient ato,
        IBackgroundJobClient jobs,
        IOptionsMonitor<AtoAgentSettings> atoAgentSettings,
        ILogger<AtoOnboardingService> logger)
    {
        _db = db;
        _ato = ato;
        _jobs = jobs;
        _atoAgentSettings = atoAgentSettings;
        _logger = logger;
    }

    public async Task EnqueueAsync(Guid renewalRequestId)
    {
        var renewal = await _db.RenewalRequests
            .Include(r => r.SearchResult)
                .ThenInclude(sr => sr.SearchLog)
            .FirstOrDefaultAsync(r => r.Id == renewalRequestId);

        if (renewal == null)
        {
            _logger.LogWarning("ATO onboarding skipped: renewal {RenewalRequestId} not found", renewalRequestId);
            return;
        }

        if (string.IsNullOrEmpty(renewal.Tfn))
        {
            _logger.LogInformation("ATO onboarding skipped: renewal {RenewalRequestId} has no TFN", renewalRequestId);
            return;
        }

        if (renewal.DateOfBirth is null)
        {
            _logger.LogInformation("ATO onboarding skipped: renewal {RenewalRequestId} has no DOB", renewalRequestId);
            return;
        }

        if (!string.IsNullOrEmpty(renewal.AtoOnboardingJobId))
        {
            _logger.LogInformation("ATO onboarding already enqueued for renewal {RenewalRequestId} (jobId {JobId})",
                renewalRequestId, renewal.AtoOnboardingJobId);
            return;
        }

        var dob = renewal.DateOfBirth.Value;
        var dobUtc = new DateTime(dob.Year, dob.Month, dob.Day, 0, 0, 0, DateTimeKind.Utc);

        var defaultAgentAbn = _atoAgentSettings.CurrentValue.DefaultAgentAbn;
        if (string.IsNullOrWhiteSpace(defaultAgentAbn))
        {
            renewal.AtoOnboardingStatus = "Failed";
            renewal.AtoOnboardingResultJson = """{"error":"No default ATO agent configured. Set one in Settings → ATO Agent before onboarding."}""";
            renewal.AtoOnboardingCompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _logger.LogWarning("ATO onboarding aborted for renewal {RenewalRequestId}: no default ATO agent configured", renewalRequestId);
            return;
        }

        var request = new EnqueueBusinessNameOnboardingRequest
        {
            IdempotencyKey = renewalRequestId.ToString("N"),
            Tfn = renewal.Tfn,
            DateOfBirth = Timestamp.FromDateTime(dobUtc),
            Abn = renewal.SearchResult?.SearchLog?.Abn ?? "",
            AgentAbn = defaultAgentAbn,
        };

        try
        {
            var response = await _ato.EnqueueBusinessNameOnboardingAsync(request);

            renewal.AtoOnboardingJobId = response.JobId;
            renewal.AtoOnboardingStatus = "Pending";
            await _db.SaveChangesAsync();

            _jobs.Schedule<IAtoOnboardingService>(
                s => s.PollAsync(renewalRequestId, response.JobId, 1),
                PollInterval);

            _logger.LogInformation("ATO onboarding enqueued for renewal {RenewalRequestId}: jobId {JobId} (created={Created})",
                renewalRequestId, response.JobId, response.Created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue ATO onboarding for renewal {RenewalRequestId}", renewalRequestId);
        }
    }

    public async Task PollAsync(Guid renewalRequestId, string jobId, int attempt)
    {
        var renewal = await _db.RenewalRequests.FirstOrDefaultAsync(r => r.Id == renewalRequestId);
        if (renewal == null)
        {
            _logger.LogWarning("ATO onboarding poll: renewal {RenewalRequestId} not found", renewalRequestId);
            return;
        }

        GetAtoJobResponse response;
        try
        {
            response = await _ato.GetAtoJobAsync(new GetAtoJobRequest { JobId = jobId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ATO onboarding poll: GetAtoJob failed for jobId {JobId} (attempt {Attempt})", jobId, attempt);
            ScheduleNextPoll(renewalRequestId, jobId, attempt);
            return;
        }

        renewal.AtoOnboardingStatus = response.Status;

        switch (response.Status)
        {
            case "Completed":
                renewal.AtoOnboardingResultJson = response.ResultJson;
                renewal.AtoOnboardingCompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                _logger.LogInformation("ATO onboarding completed for renewal {RenewalRequestId}", renewalRequestId);
                return;

            case "Failed":
                renewal.AtoOnboardingResultJson = response.ResultJson;
                renewal.AtoOnboardingCompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                _logger.LogWarning("ATO onboarding failed for renewal {RenewalRequestId}: {Error}",
                    renewalRequestId, response.LastError);
                return;

            default:
                await _db.SaveChangesAsync();
                ScheduleNextPoll(renewalRequestId, jobId, attempt);
                return;
        }
    }

    private void ScheduleNextPoll(Guid renewalRequestId, string jobId, int attempt)
    {
        if (attempt >= MaxPollAttempts)
        {
            _logger.LogWarning("ATO onboarding poll: gave up on renewal {RenewalRequestId} jobId {JobId} after {Attempts} attempts",
                renewalRequestId, jobId, attempt);
            return;
        }

        _jobs.Schedule<IAtoOnboardingService>(
            s => s.PollAsync(renewalRequestId, jobId, attempt + 1),
            PollInterval);
    }
}
