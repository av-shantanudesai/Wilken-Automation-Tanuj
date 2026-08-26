using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Worker;

/// <summary>Re-queues hung RUNNING jobs while the loop is busy. Does not delete logs, files, or database rows.</summary>
public class MaintenanceHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly MaintenanceSettings _settings;
    private readonly WorkerSettings _worker;
    private readonly WorkerState _state;
    private readonly ILogger<MaintenanceHostedService> _logger;

    public MaintenanceHostedService(
        IServiceScopeFactory scopes,
        MaintenanceSettings settings,
        WorkerSettings worker,
        WorkerState state,
        ILogger<MaintenanceHostedService> logger)
    {
        _scopes = scopes;
        _settings = settings;
        _worker = worker;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(_settings.IntervalMinutes, 5, 180));
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var recovery = scope.ServiceProvider.GetRequiredService<StartupRecoveryService>();
                var hungAfter = DateTime.UtcNow.AddMinutes(-Math.Clamp(_worker.HungJobTimeoutMinutes, 15, 180));
                var recovered = await recovery.RecoverStaleRunningJobsAsync(
                    stoppingToken, hungAfter, _state.Snapshot().CurrentJobId);
                if (recovered > 0)
                    _logger.LogWarning("Maintenance hung-job recovery: {Count} job(s) re-queued.", recovered);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Worker maintenance cycle failed; will retry next interval.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
