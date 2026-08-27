using System.Net.Http.Json;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Worker;

/// <summary>Publishes the worker heartbeat (status + current runtime) through SignalR and HTTP.</summary>
public class HeartbeatService : BackgroundService
{
    /// <summary>Reissue the cached worker token this long before it expires.</summary>
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(5);

    private readonly WorkerState _state;
    private readonly IRealtimeNotifier _notifier;
    private readonly WorkerSettings _settings;
    private readonly JwtTokenService _tokens;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private string? _cachedToken;
    private DateTime _cachedTokenExpiresUtc = DateTime.MinValue;

    public HeartbeatService(
        WorkerState state,
        IRealtimeNotifier notifier,
        WorkerSettings settings,
        JwtTokenService tokens,
        ILogger<HeartbeatService> logger)
    {
        _state = state;
        _notifier = notifier;
        _settings = settings;
        _tokens = tokens;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.HeartbeatIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var snapshot = _state.Snapshot();
            await _notifier.PublishWorkerStatusAsync(snapshot, stoppingToken, _state.OwnerUserId);
            await PostHeartbeatAsync(snapshot, stoppingToken);
        }
    }

    private async Task PostHeartbeatAsync(WorkerStatusDto snapshot, CancellationToken ct)
    {
        try
        {
            var url = $"{_settings.ApiBaseUrl.TrimEnd('/')}/api/worker/heartbeat";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", WorkerToken());
            request.Content = JsonContent.Create(snapshot);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogDebug("HTTP heartbeat returned {Status}.", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug("HTTP heartbeat failed: {Message}", ex.Message);
        }
    }

    /// <summary>Reuses one worker token per validity window instead of minting a new JWT every beat.</summary>
    private string WorkerToken()
    {
        if (_cachedToken is null || DateTime.UtcNow >= _cachedTokenExpiresUtc - TokenRefreshMargin)
        {
            _cachedToken = _tokens.CreateWorkerToken();
            _cachedTokenExpiresUtc = DateTime.UtcNow.AddHours(_tokens.WorkerTokenHours);
        }
        return _cachedToken;
    }

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }
}
