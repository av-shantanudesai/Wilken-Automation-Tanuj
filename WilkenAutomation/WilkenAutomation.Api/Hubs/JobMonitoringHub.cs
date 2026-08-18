using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Hubs;

/// <summary>
/// Real-time hub at /hubs/job-monitoring.
/// Dashboard clients authenticate with a user JWT and only listen.
/// The worker agent connects with a Worker-role JWT and invokes Publish*.
/// </summary>
[Authorize]
public class JobMonitoringHub : Hub
{
    private readonly WorkerStatusRegistry _registry;

    public JobMonitoringHub(WorkerStatusRegistry registry)
    {
        _registry = registry;
    }

    [Authorize(Roles = AuthRoles.Worker)]
    public Task PublishEvent(string eventName, object payload) =>
        Clients.Others.SendAsync(eventName, payload);

    [Authorize(Roles = AuthRoles.Worker)]
    public async Task PublishWorkerStatus(WorkerStatusDto status)
    {
        _registry.Update(status);
        await Clients.Others.SendAsync(SignalREvents.WorkerStatusChanged, status);
        await Clients.Others.SendAsync(SignalREvents.WilkenSessionChanged,
            new { wilkenSessionStatus = status.WilkenSessionStatus });
    }
}
