using Microsoft.AspNetCore.SignalR;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Hubs;

/// <summary>
/// Real-time hub at /hubs/job-monitoring.
/// Dashboard clients only listen. The worker agent connects as a client too and
/// invokes the Publish* methods; the hub relays events to all dashboard clients.
/// The database is always persisted first - these events are notifications only.
/// </summary>
public class JobMonitoringHub : Hub
{
    private readonly WorkerStatusRegistry _registry;

    public JobMonitoringHub(WorkerStatusRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Called by the worker agent to relay a job/run event to dashboards.</summary>
    public Task PublishEvent(string eventName, object payload) =>
        Clients.Others.SendAsync(eventName, payload);

    /// <summary>Called by the worker agent as heartbeat; cached for REST consumers.</summary>
    public async Task PublishWorkerStatus(WorkerStatusDto status)
    {
        _registry.Update(status);
        await Clients.Others.SendAsync(SignalREvents.WorkerStatusChanged, status);
        await Clients.Others.SendAsync(SignalREvents.WilkenSessionChanged,
            new { wilkenSessionStatus = status.WilkenSessionStatus });
    }
}
