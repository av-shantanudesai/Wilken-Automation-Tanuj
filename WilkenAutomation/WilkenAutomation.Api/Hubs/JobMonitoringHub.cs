using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WilkenAutomation.Api.Auth;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Hubs;

/// <summary>
/// Real-time hub at /hubs/job-monitoring.
/// Dashboard clients join a per-user group. Job events require SubscribeRun
/// (ownership-checked) so they are not delivered to other customers.
/// Publish* requires the Worker role.
/// </summary>
[Authorize]
public class JobMonitoringHub : Hub
{
    private readonly WorkerStatusRegistry _registry;
    private readonly IRunRepository _runs;

    public JobMonitoringHub(WorkerStatusRegistry registry, IRunRepository runs)
    {
        _registry = registry;
        _runs = runs;
    }

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(AuthRoles.Worker) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Workers);
        else
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(Context.User!.GetRequiredUserId()));

        await base.OnConnectedAsync();
    }

    /// <summary>Dashboard clients subscribe to a run they own before receiving job events.</summary>
    public async Task SubscribeRun(string runId)
    {
        if (Context.User?.IsInRole(AuthRoles.Worker) == true) return;
        if (string.IsNullOrWhiteSpace(runId)) return;
        if (await OwnedRunAsync(runId) is null)
            throw new HubException("Run not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Run(runId.Trim()));
    }

    public async Task UnsubscribeRun(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Run(runId.Trim()));
    }

    [Authorize(Roles = AuthRoles.Worker)]
    public async Task PublishEvent(string eventName, object payload, long userId, string? runId)
    {
        if (userId <= 0 || !SignalREvents.IsKnown(eventName))
            return;

        if (!string.IsNullOrWhiteSpace(runId))
        {
            var run = await _runs.GetByRunIdAsync(runId, Context.ConnectionAborted);
            if (run is null || run.UserId != userId)
                return;
            await Clients.Group(HubGroups.Run(run.RunId)).SendAsync(eventName, payload);
            if (eventName == SignalREvents.RunsChanged)
                await Clients.Group(HubGroups.User(userId)).SendAsync(eventName, payload);
            return;
        }

        if (eventName == SignalREvents.RunsChanged)
            await Clients.Group(HubGroups.User(userId)).SendAsync(eventName, payload);
    }

    [Authorize(Roles = AuthRoles.Worker)]
    public async Task PublishWorkerStatus(WorkerStatusDto status, long userId)
    {
        if (status is null) return;
        _registry.Update(status);

        if (userId <= 0 || string.IsNullOrWhiteSpace(status.CurrentRunId))
            return;

        var run = await _runs.GetByRunIdAsync(status.CurrentRunId, Context.ConnectionAborted);
        if (run is null || run.UserId != userId)
            return;

        var scoped = WorkerStatusScope.ForOwner(status);
        await Clients.Group(HubGroups.User(userId)).SendAsync(SignalREvents.WorkerStatusChanged, scoped);
        await Clients.Group(HubGroups.Run(run.RunId)).SendAsync(SignalREvents.WorkerStatusChanged, scoped);
        await Clients.Group(HubGroups.User(userId)).SendAsync(SignalREvents.WilkenSessionChanged,
            new { wilkenSessionStatus = scoped.WilkenSessionStatus, runId = run.RunId });
        await Clients.Group(HubGroups.Run(run.RunId)).SendAsync(SignalREvents.WilkenSessionChanged,
            new { wilkenSessionStatus = scoped.WilkenSessionStatus, runId = run.RunId });
    }

    private async Task<AutomationRun?> OwnedRunAsync(string runId)
    {
        var run = await _runs.GetByRunIdAsync(runId.Trim(), Context.ConnectionAborted);
        if (run is null || run.UserId != Context.User!.GetRequiredUserId())
            return null;
        return run;
    }
}
