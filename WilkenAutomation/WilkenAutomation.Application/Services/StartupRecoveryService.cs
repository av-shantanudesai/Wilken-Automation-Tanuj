using Microsoft.Extensions.Logging;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Restart recovery, executed on worker startup:
///  1. Stale RUNNING jobs (interrupted by crash/restart) -> RETRY, attempt marked Interrupted.
///  2. Successful jobs are re-verified: export file must still exist and its
///     SHA-256 must match; otherwise the job is re-queued instead of being trusted.
/// Completed validated exports are never regenerated.
/// </summary>
public class StartupRecoveryService
{
    private readonly IJobRepository _jobs;
    private readonly IRunRepository _runs;
    private readonly ILogRepository _logs;
    private readonly IChecksumService _checksum;
    private readonly ILogger<StartupRecoveryService> _logger;

    public StartupRecoveryService(
        IJobRepository jobs, IRunRepository runs, ILogRepository logs,
        IChecksumService checksum, ILogger<StartupRecoveryService> logger)
    {
        _jobs = jobs;
        _runs = runs;
        _logs = logs;
        _checksum = checksum;
        _logger = logger;
    }

    public async Task<int> RecoverStaleRunningJobsAsync(CancellationToken ct)
    {
        var stale = await _jobs.GetStaleRunningAsync(ct);
        foreach (var job in stale)
        {
            var attempts = await _jobs.GetAttemptsAsync(job.JobId, ct);
            var open = attempts.FirstOrDefault(a => a.Status == "Running");
            if (open is not null)
            {
                open.Status = "Interrupted";
                open.EndTime = DateTime.UtcNow;
                open.ErrorCode = "INTERRUPTED";
                open.ErrorMessage = "Attempt interrupted by worker/system restart.";
                await _jobs.UpdateAttemptAsync(open, ct);
            }

            JobStateMachine.EnsureTransition(JobStatus.Running, JobStatus.Retry);
            job.Status = JobStatus.Retry;
            job.ErrorCode = "INTERRUPTED";
            job.ErrorMessage = "Job was RUNNING during a restart and has been re-queued.";
            job.ApplicationState = ApplicationState.Idle.ToWireName();
            job.UpdatedAt = DateTime.UtcNow;
            await _jobs.UpdateAsync(job, ct);
            await _runs.RefreshCountersAsync(job.RunId, ct);

            await _logs.AddAsync(new AutomationLog
            {
                RunId = job.RunId,
                JobId = job.JobId,
                Client = job.Client,
                FiscalYear = job.FiscalYear,
                Department = job.Department,
                Timestamp = DateTime.UtcNow,
                Level = "WARN",
                Action = "RestartRecovery",
                Message = "Stale RUNNING job detected after restart -> RETRY."
            }, ct);

            _logger.LogWarning("Restart recovery: stale RUNNING job {JobId} moved to RETRY.", job.JobId);
        }
        return stale.Count;
    }

    /// <summary>Returns the number of successful jobs that failed re-verification and were re-queued.</summary>
    public async Task<int> VerifyCompletedJobsAsync(string runId, CancellationToken ct)
    {
        var requeued = 0;
        foreach (var job in await _jobs.GetSuccessfulAsync(runId, ct))
        {
            var reason = await VerifyAsync(job, ct);
            if (reason is null) continue;

            JobStateMachine.EnsureTransition(job.Status, JobStatus.Pending);
            job.Status = JobStatus.Pending;
            job.ErrorCode = "REVERIFICATION_FAILED";
            job.ErrorMessage = reason;
            job.ValidationStatus = ValidationStatus.NotValidated;
            job.Sha256 = null;
            job.UpdatedAt = DateTime.UtcNow;
            await _jobs.UpdateAsync(job, ct);

            await _logs.AddAsync(new AutomationLog
            {
                RunId = job.RunId,
                JobId = job.JobId,
                Client = job.Client,
                FiscalYear = job.FiscalYear,
                Department = job.Department,
                Timestamp = DateTime.UtcNow,
                Level = "WARN",
                Action = "ReverifyFailed",
                Message = $"Completed job re-queued: {reason}"
            }, ct);
            requeued++;
        }

        if (requeued > 0) await _runs.RefreshCountersAsync(runId, ct);
        return requeued;
    }

    private async Task<string?> VerifyAsync(ExportJob job, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(job.FilePath))
            return "No export file recorded for a successful job.";
        if (!File.Exists(job.FilePath))
            return $"Export file missing: {job.FilePath}";
        if (!string.IsNullOrEmpty(job.Sha256))
        {
            var actual = await _checksum.ComputeSha256Async(job.FilePath, ct);
            if (!string.Equals(actual, job.Sha256, StringComparison.OrdinalIgnoreCase))
                return "SHA-256 mismatch - export file was modified after validation.";
        }
        return null;
    }
}
