using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Api.Services;

/// <summary>
/// Latest worker heartbeat, kept in memory in the API process. The worker agent
/// publishes it over SignalR every few seconds; REST endpoints read it from here.
/// A stale heartbeat (worker gone) degrades to STOPPED.
/// </summary>
public class WorkerStatusRegistry
{
    private readonly object _lock = new();
    private WorkerStatusDto? _latest;
    private static readonly TimeSpan Staleness = TimeSpan.FromSeconds(15);

    public void Update(WorkerStatusDto status)
    {
        lock (_lock)
        {
            status.HeartbeatAt = DateTime.UtcNow;
            _latest = status;
        }
    }

    public WorkerStatusDto Snapshot()
    {
        lock (_lock)
        {
            if (_latest is null || DateTime.UtcNow - _latest.HeartbeatAt > Staleness)
            {
                return new WorkerStatusDto
                {
                    WorkerState = "STOPPED",
                    WilkenSessionStatus = "NotRunning",
                    HeartbeatAt = _latest?.HeartbeatAt ?? DateTime.MinValue
                };
            }
            return _latest;
        }
    }
}
