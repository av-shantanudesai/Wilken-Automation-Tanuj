using Microsoft.AspNetCore.SignalR.Client;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Realtime;

/// <summary>
/// Worker-side notifier: connects to the API's /hubs/job-monitoring hub as a
/// client and relays events to a specific user/run. The database is always
/// persisted first, so a lost connection only delays UI updates.
/// </summary>
public class SignalRNotifier : IRealtimeNotifier, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<SignalRNotifier> _logger;

    public SignalRNotifier(WorkerSettings settings, JwtTokenService tokens, ILogger<SignalRNotifier> logger)
    {
        _logger = logger;
        _connection = new HubConnectionBuilder()
            .WithUrl($"{settings.ApiBaseUrl.TrimEnd('/')}/hubs/job-monitoring", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(tokens.CreateWorkerToken());
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();
    }

    public async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connection.State != HubConnectionState.Disconnected) return;
        try
        {
            await _connection.StartAsync(ct);
            _logger.LogInformation("Connected to API SignalR hub.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SignalR hub not reachable yet: {Message}", ex.Message);
        }
    }

    public async Task PublishAsync(string eventName, object payload, CancellationToken ct = default, long? audienceUserId = null, string? runId = null)
    {
        await EnsureConnectedAsync(ct);
        if (_connection.State != HubConnectionState.Connected) return;
        if (audienceUserId is not > 0 || !SignalREvents.IsKnown(eventName)) return;
        try
        {
            await _connection.InvokeAsync("PublishEvent", eventName, payload, audienceUserId.Value, runId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to publish {Event}: {Message}", eventName, ex.Message);
        }
    }

    public async Task PublishWorkerStatusAsync(WorkerStatusDto status, CancellationToken ct = default, long? audienceUserId = null)
    {
        await EnsureConnectedAsync(ct);
        if (_connection.State != HubConnectionState.Connected) return;
        try
        {
            await _connection.InvokeAsync("PublishWorkerStatus", status, audienceUserId ?? 0, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to publish worker status: {Message}", ex.Message);
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

/// <summary>Never give up reconnecting to the API hub during a long unattended run.</summary>
internal sealed class ForeverRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var seconds = Math.Min(30, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5)));
        return TimeSpan.FromSeconds(seconds);
    }
}
