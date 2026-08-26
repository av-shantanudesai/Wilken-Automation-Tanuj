using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Dashboard mutations that are not HTTP concerns: start/pause, requeue, retry-failed.
/// Controllers map results to status codes; they do not mutate entities.
/// </summary>
public sealed class RunLifecycleService
{
    private readonly IRunRepository _runs;
    private readonly IJobRepository _jobs;
    private readonly IRealtimeNotifier _notifier;

    public RunLifecycleService(IRunRepository runs, IJobRepository jobs, IRealtimeNotifier notifier)
    {
        _runs = runs;
        _jobs = jobs;
        _notifier = notifier;
    }

    public async Task<AutomationRun?> StartAsync(string runId, long userId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, userId, ct);
        if (run is null) return null;

        if (run.Status is RunStatus.Created or RunStatus.Paused or RunStatus.Completed)
        {
            run.Status = RunStatus.Running;
            run.StartedAt ??= DateTime.UtcNow;
            run.CompletedAt = null;
            await _runs.UpdateAsync(run, ct);
            await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId }, ct, run.UserId, runId);
        }

        return run;
    }

    public async Task<AutomationRun?> PauseAsync(string runId, long userId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, userId, ct);
        if (run is null) return null;

        if (run.Status == RunStatus.Running)
        {
            run.Status = RunStatus.Paused;
            await _runs.UpdateAsync(run, ct);
            await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId }, ct, run.UserId, runId);
        }

        return run;
    }

    public async Task<ExportJob?> RequeueJobAsync(string jobId, long userId, CancellationToken ct)
    {
        var job = await _jobs.GetByJobIdAsync(jobId, ct);
        if (job is null) return null;

        var run = await OwnedRunAsync(job.RunId, userId, ct);
        if (run is null) return null;

        if (!JobStateMachine.CanTransition(job.Status, JobStatus.Pending))
            throw new InvalidOperationException($"Job in status {job.Status} cannot be re-queued.");

        job.Status = JobStatus.Pending;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _jobs.UpdateAsync(job, ct);

        if (run.Status == RunStatus.Completed)
        {
            run.Status = RunStatus.Running;
            run.CompletedAt = null;
            await _runs.UpdateAsync(run, ct);
        }

        await _runs.RefreshCountersAsync(job.RunId, ct);
        await _notifier.PublishAsync(SignalREvents.JobStatusChanged, job.ToDto(), ct, run.UserId, job.RunId);
        await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId = job.RunId }, ct, run.UserId, job.RunId);
        return job;
    }

    public async Task<AutomationRun?> RetryFailedAsync(string runId, long userId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, userId, ct);
        if (run is null) return null;

        var requeued = await _jobs.RequeueFailedJobsAsync(runId, ct);
        if (requeued > 0)
        {
            if (run.Status == RunStatus.Completed)
            {
                run.Status = RunStatus.Running;
                run.CompletedAt = null;
                await _runs.UpdateAsync(run, ct);
            }

            await _runs.RefreshCountersAsync(runId, ct);
            await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId }, ct, run.UserId, runId);
        }

        return run;
    }

    private async Task<AutomationRun?> OwnedRunAsync(string runId, long userId, CancellationToken ct)
    {
        var run = await _runs.GetByRunIdAsync(runId, ct);
        if (run is null || run.UserId != userId) return null;
        return run;
    }
}
