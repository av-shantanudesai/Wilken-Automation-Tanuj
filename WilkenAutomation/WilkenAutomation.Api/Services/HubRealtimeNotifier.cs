using Microsoft.AspNetCore.SignalR;
using WilkenAutomation.Api.Hubs;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Services;

/// <summary>API-side notifier: never uses Clients.All. Job events go to the run group; run-list events go to the owning user.</summary>
public class HubRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<JobMonitoringHub> _hub;

    public HubRealtimeNotifier(IHubContext<JobMonitoringHub> hub)
    {
        _hub = hub;
    }

    public async Task PublishAsync(string eventName, object payload, CancellationToken ct = default, long? audienceUserId = null, string? runId = null)
    {
        if (audienceUserId is not > 0 || !SignalREvents.IsKnown(eventName))
            return;

        if (!string.IsNullOrWhiteSpace(runId))
            await _hub.Clients.Group(HubGroups.Run(runId)).SendAsync(eventName, payload, ct);

        if (eventName == SignalREvents.RunsChanged || string.IsNullOrWhiteSpace(runId))
            await _hub.Clients.Group(HubGroups.User(audienceUserId.Value)).SendAsync(eventName, payload, ct);
    }

    public async Task PublishWorkerStatusAsync(WorkerStatusDto status, CancellationToken ct = default, long? audienceUserId = null)
    {
        if (audienceUserId is not > 0 || status is null || string.IsNullOrWhiteSpace(status.CurrentRunId))
            return;

        var scoped = WorkerStatusScope.ForOwner(status);
        await _hub.Clients.Group(HubGroups.User(audienceUserId.Value))
            .SendAsync(SignalREvents.WorkerStatusChanged, scoped, ct);
        await _hub.Clients.Group(HubGroups.Run(status.CurrentRunId))
            .SendAsync(SignalREvents.WorkerStatusChanged, scoped, ct);
    }
}

/// <summary>Strips fields that must not leak across customers on the worker heartbeat.</summary>
public static class WorkerStatusScope
{
    public static WorkerStatusDto ForOwner(WorkerStatusDto status) => new()
    {
        WorkerState = status.WorkerState,
        WilkenSessionStatus = status.WilkenSessionStatus,
        AutomationMode = status.AutomationMode,
        CurrentRunId = status.CurrentRunId,
        CurrentJobId = status.CurrentJobId,
        CurrentJobLabel = status.CurrentJobLabel,
        CurrentAttempt = status.CurrentAttempt,
        CurrentAction = status.CurrentAction,
        CurrentJobStartedAt = status.CurrentJobStartedAt,
        RuntimeSeconds = status.RuntimeSeconds,
        HeartbeatAt = status.HeartbeatAt
    };

    public static WorkerStatusDto ForStranger(WorkerStatusDto status) => new()
    {
        WorkerState = status.WorkerState,
        WilkenSessionStatus = status.WilkenSessionStatus,
        AutomationMode = status.AutomationMode,
        HeartbeatAt = status.HeartbeatAt
    };
}
