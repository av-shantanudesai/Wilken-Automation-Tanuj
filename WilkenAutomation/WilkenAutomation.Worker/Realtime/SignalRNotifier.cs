using Microsoft.AspNetCore.SignalR.Client;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Realtime;

/// <summary>
/// Worker-side notifier: connects to the API's /hubs/job-monitoring hub as a
/// client and relays events. The database is always persisted first, so a lost
/// connection only delays UI updates - it never loses state.
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
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)
            })
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

    public async Task PublishAsync(string eventName, object payload, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        if (_connection.State != HubConnectionState.Connected) return;
        try
        {
            await _connection.InvokeAsync("PublishEvent", eventName, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to publish {Event}: {Message}", eventName, ex.Message);
        }
    }

    public async Task PublishWorkerStatusAsync(WorkerStatusDto status, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        if (_connection.State != HubConnectionState.Connected) return;
        try
        {
            await _connection.InvokeAsync("PublishWorkerStatus", status, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to publish worker status: {Message}", ex.Message);
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
