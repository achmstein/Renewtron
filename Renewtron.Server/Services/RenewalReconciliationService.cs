using Hangfire;
using Hangfire.Common;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// Repairs the divergence between renewal rows and the Hangfire queue. Rows get marooned
/// when a job is lost across a deploy, expires, or the process dies mid-run — production
/// accumulated 164 such rows before this existed. Pending orphans are re-queued (capped
/// per run); anything that may already have paid ASIC is flagged for a human instead.
/// </summary>
public class RenewalReconciliationService : IRenewalReconciliationService
{
    // Pending rows normally start processing within seconds; Processing runs take
    // minutes (up to ~15 with an OTP wait). Anything older has fallen out of the queue.
    private static readonly TimeSpan PendingGrace = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProcessingGrace = TimeSpan.FromHours(2);

    private readonly ApplicationDbContext _dbContext;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<RenewalReconciliationService> _logger;

    public RenewalReconciliationService(
        ApplicationDbContext dbContext,
        IBackgroundJobClient backgroundJobClient,
        ILogger<RenewalReconciliationService> logger)
    {
        _dbContext = dbContext;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task<RenewalReconciliationReport> ReconcileAsync(bool dryRun, int maxRequeue)
    {
        var now = DateTime.UtcNow;
        var pendingCutoff = now - PendingGrace;
        var processingCutoff = now - ProcessingGrace;

        var stale = await _dbContext.RenewalRequests
            .Include(r => r.SearchResult).ThenInclude(sr => sr.SearchLog)
            .Where(r =>
                (r.Status == RenewalStatus.Pending && r.InitiatedAt < pendingCutoff) ||
                (r.Status == RenewalStatus.Processing && r.InitiatedAt < processingCutoff))
            .OrderBy(r => r.InitiatedAt)
            .ToListAsync();

        var liveJobIds = GetLiveRenewalJobIds();

        var items = new List<RenewalReconciliationItem>();
        int requeued = 0, needsVerification = 0, markedStale = 0, skippedLive = 0, capped = 0;

        foreach (var renewal in stale)
        {
            if (liveJobIds.Contains(renewal.Id))
            {
                skippedLive++;
                continue;
            }

            var ageHours = (int)(now - renewal.InitiatedAt).TotalHours;
            string action;

            if (renewal.HostedTokenizationId != null)
            {
                // The run reached the payment gateway before dying — ASIC may hold a
                // payment, so a blind re-run risks paying twice. Flag for a human.
                action = "needs-verification";
                needsVerification++;
                if (!dryRun)
                {
                    renewal.Status = RenewalStatus.Failed;
                    renewal.FailedAtStep = "Needs Verification";
                    renewal.ErrorMessage = "Reconciliation: the run stalled after the payment gateway was opened. Verify at ASIC whether payment was taken before retrying.";
                }
            }
            else if (renewal.Status == RenewalStatus.Processing)
            {
                // Crashed mid-run at an unknown step. Surface it as an explicit failure
                // the operator can see and verify, rather than leaving it as fake "in flight".
                action = "marked-stale";
                markedStale++;
                if (!dryRun)
                {
                    renewal.Status = RenewalStatus.Failed;
                    renewal.FailedAtStep = "Stale Processing";
                    renewal.ErrorMessage = "Reconciliation: renewal was stuck in Processing with no live job (crash or restart mid-run). Verify at ASIC before retrying.";
                }
            }
            else if (requeued < maxRequeue)
            {
                // Pending with no job and no payment exposure: safe to re-queue as-is.
                action = "requeued";
                requeued++;
                if (!dryRun)
                    _backgroundJobClient.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewal.Id));
            }
            else
            {
                action = "requeue-capped";
                capped++;
            }

            items.Add(new RenewalReconciliationItem(
                renewal.Id,
                renewal.SearchResult?.BusinessName,
                renewal.SearchResult?.SearchLog?.Abn,
                renewal.Amount,
                renewal.Status.ToString(),
                ageHours,
                action));
        }

        if (!dryRun)
            await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Renewal reconciliation ({Mode}): scanned {Scanned}, requeued {Requeued}, needs-verification {NeedsVerification}, marked-stale {MarkedStale}, capped {Capped}, live-job {SkippedLive}",
            dryRun ? "dry-run" : "live", stale.Count, requeued, needsVerification, markedStale, capped, skippedLive);

        return new RenewalReconciliationReport(
            dryRun, stale.Count, skippedLive, requeued, needsVerification, markedStale, capped,
            items.Take(500).ToList());
    }

    /// <summary>
    /// Renewal ids that currently have a job anywhere in Hangfire (enqueued, fetched,
    /// scheduled for retry, or executing) — these are alive and must not be touched.
    /// </summary>
    private static HashSet<Guid> GetLiveRenewalJobIds()
    {
        var ids = new HashSet<Guid>();
        var monitoring = JobStorage.Current.GetMonitoringApi();

        void Collect(Job? job)
        {
            if (job?.Method?.Name == nameof(IRenewalProcessingService.ProcessRenewalAsync)
                && job.Args is { Count: > 0 } && job.Args[0] is Guid id)
            {
                ids.Add(id);
            }
        }

        foreach (var queue in monitoring.Queues())
        {
            foreach (var entry in monitoring.EnqueuedJobs(queue.Name, 0, 1000)) Collect(entry.Value?.Job);
            foreach (var entry in monitoring.FetchedJobs(queue.Name, 0, 1000)) Collect(entry.Value?.Job);
        }
        foreach (var entry in monitoring.ScheduledJobs(0, 1000)) Collect(entry.Value?.Job);
        foreach (var entry in monitoring.ProcessingJobs(0, 1000)) Collect(entry.Value?.Job);

        return ids;
    }
}
