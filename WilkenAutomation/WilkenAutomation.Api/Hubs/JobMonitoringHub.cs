using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WilkenAutomation.Api.Auth;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Hubs;

/// <summary>
/// Real-time hub at /hubs/job-monitoring.
/// Dashboard clients join a per-user group and only receive their own job events.
/// Worker status is shared (one worker process). Publish* requires the Worker role.
/// </summary>
[Authorize]
public class JobMonitoringHub : Hub
{
    private readonly WorkerStatusRegistry _registry;

    public JobMonitoringHub(WorkerStatusRegistry registry)
    {
        _registry = registry;
    }

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(AuthRoles.Worker) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Workers);
        else
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(Context.User!.GetRequiredUserId()));

        await base.OnConnectedAsync();
    }

    [Authorize(Roles = AuthRoles.Worker)]
    public Task PublishEvent(string eventName, object payload, long userId)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(eventName))
            return Task.CompletedTask;
        return Clients.Group(HubGroups.User(userId)).SendAsync(eventName, payload);
    }

    [Authorize(Roles = AuthRoles.Worker)]
    public async Task PublishWorkerStatus(WorkerStatusDto status)
    {
        _registry.Update(status);
        await Clients.All.SendAsync(SignalREvents.WorkerStatusChanged, status);
        await Clients.All.SendAsync(SignalREvents.WilkenSessionChanged,
            new { wilkenSessionStatus = status.WilkenSessionStatus });
    }
}
