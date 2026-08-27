namespace WilkenAutomation.Application.Models;

/// <summary>Heartbeat the worker publishes; also served via GET /api/worker/status.</summary>
public class WorkerStatusDto
{
    public string WorkerState { get; set; } = "STOPPED";
    public string WilkenSessionStatus { get; set; } = "NotRunning";
    public string AutomationMode { get; set; } = "Mock";
    public string? CurrentRunId { get; set; }
    public string? CurrentJobId { get; set; }
    public string? CurrentJobLabel { get; set; }
    public int? CurrentAttempt { get; set; }
    public string? CurrentAction { get; set; }
    public DateTime? CurrentJobStartedAt { get; set; }
    public double? RuntimeSeconds { get; set; }
    public string? LastSuccessJobId { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public DateTime HeartbeatAt { get; set; }
}
