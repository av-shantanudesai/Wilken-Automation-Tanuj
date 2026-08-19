using System.Text.Json;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Worker;

/// <summary>
/// The single-worker job loop (WorkerCount=1 in version 1):
///   startup recovery -> claim next eligible job of the active run -> execute ->
///   recover session if lost -> repeat. A failed job never stops the loop.
/// </summary>
public class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWilkenAutomationService _wilken;
    private readonly WorkerState _state;
    private readonly WorkerSettings _settings;
    private readonly ILogger<JobWorker> _logger;
    private readonly HashSet<string> _verifiedRuns = new();

    public JobWorker(
        IServiceScopeFactory scopeFactory,
        IWilkenAutomationService wilken,
        WorkerState state,
        WorkerSettings settings,
        ILogger<JobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _wilken = wilken;
        _state = state;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state.SetStatus(WorkerStatus.Starting);
        _logger.LogInformation(
            "Worker agent starting (mode: {Mode}, workers: {Count}). {Hint}",
            _settings.AutomationMode,
            _settings.WorkerCount,
            _settings.AutomationMode == AutomationMode.Mock
                ? "Mock mode does not open a window. Use: dotnet run --project WilkenAutomation.Worker -- --desktop-test"
                : _settings.AutomationMode == AutomationMode.DesktopTest
                    ? "DesktopTest mode will launch/attach WilkenAutomation.TestDesktop."
                    : "Wilken mode will launch/attach the real Wilken executable.");

        await RunStartupRecoveryAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextJobAsync(stoppingToken);
                if (!processed)
                {
                    _state.SetStatus(WorkerStatus.Idle);
                    _state.SetCurrentJob(null);
                    await Task.Delay(_settings.PollIntervalMs, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let an unexpected error kill the loop.
                _logger.LogError(ex, "Unexpected error in worker loop.");
                _state.SetStatus(WorkerStatus.Error);
                _state.ReportError(ex.Message);
                await Task.Delay(Math.Max(2000, _settings.PollIntervalMs), stoppingToken);
            }
        }

        _state.SetStatus(WorkerStatus.Stopped);
        _logger.LogInformation("Worker agent stopped.");
    }

    private async Task RunStartupRecoveryAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recovery = scope.ServiceProvider.GetRequiredService<StartupRecoveryService>();
            var recovered = await recovery.RecoverStaleRunningJobsAsync(ct);
            if (recovered > 0)
                _logger.LogWarning("Restart recovery: {Count} stale RUNNING job(s) re-queued.", recovered);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Startup recovery failed - continuing; jobs remain recoverable.");
        }
    }

    /// <summary>Returns true when a job was processed, false when there was nothing to do.</summary>
    private async Task<bool> ProcessNextJobAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        var run = await runs.GetActiveRunAsync(ct);
        if (run is null) return false;

        // Re-verify previously successful exports once per run per worker session.
        if (_verifiedRuns.Add(run.RunId))
        {
            var recovery = scope.ServiceProvider.GetRequiredService<StartupRecoveryService>();
            var requeued = await recovery.VerifyCompletedJobsAsync(run.RunId, ct);
            if (requeued > 0)
                _logger.LogWarning("Run {RunId}: {Count} completed job(s) failed file re-verification and were re-queued.",
                    run.RunId, requeued);
        }

        var job = await jobs.GetNextEligibleAsync(run.RunId, ct);
        if (job is null)
        {
            await runs.RefreshCountersAsync(run.RunId, ct);
            return false;
        }

        var config = JsonSerializer.Deserialize<RunConfig>(run.ConfigJson) ?? new RunConfig();

        _state.SetStatus(WorkerStatus.Running);
        _state.SetCurrentJob(job, run.UserId);

        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
        executor.ApplicationStateChanged += (j, appState) =>
        {
            _state.SetCurrentJob(j);
            _state.SetAction(appState.ToWireName());
            _state.SetSession(_wilken.SessionStatus);
            return Task.CompletedTask;
        };

        var result = await executor.ExecuteAsync(job, config, ct);
        _state.SetSession(_wilken.SessionStatus);

        if (result.FinalStatus.IsSuccess())
        {
            _state.ReportSuccess(job.JobId);
        }
        else
        {
            _state.ReportError($"{job.JobId}: {job.ErrorMessage}");
            if (result.SessionLost)
            {
                _state.SetStatus(WorkerStatus.Recovering);
                _state.SetSession(WilkenSessionStatus.Recovering);
                _logger.LogWarning("Wilken session lost - recovering before next job.");
                try
                {
                    await _wilken.RecoverSessionAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Session recovery failed; will retry with the next job.");
                }
                _state.SetSession(_wilken.SessionStatus);
            }
        }

        _state.SetCurrentJob(null);
        return true;
    }
}

/// <summary>Publishes the worker heartbeat (status + current runtime) through SignalR.</summary>
public class HeartbeatService : BackgroundService
{
    private readonly WorkerState _state;
    private readonly IRealtimeNotifier _notifier;
    private readonly WorkerSettings _settings;

    public HeartbeatService(WorkerState state, IRealtimeNotifier notifier, WorkerSettings settings)
    {
        _state = state;
        _notifier = notifier;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.HeartbeatIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _notifier.PublishWorkerStatusAsync(_state.Snapshot(), stoppingToken, _state.OwnerUserId);
        }
    }
}
