using Microsoft.AspNetCore.SignalR;
using WilkenAutomation.Api.Hubs;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Services;

/// <summary>API-side notifier: job events go to the owning user group only.</summary>
public class HubRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<JobMonitoringHub> _hub;

    public HubRealtimeNotifier(IHubContext<JobMonitoringHub> hub)
    {
        _hub = hub;
    }

    public Task PublishAsync(string eventName, object payload, CancellationToken ct = default, long? audienceUserId = null)
    {
        if (audienceUserId is > 0)
            return _hub.Clients.Group(HubGroups.User(audienceUserId.Value)).SendAsync(eventName, payload, ct);
        return Task.CompletedTask;
    }

    public Task PublishWorkerStatusAsync(WorkerStatusDto status, CancellationToken ct = default) =>
        _hub.Clients.All.SendAsync(SignalREvents.WorkerStatusChanged, status, ct);
}
