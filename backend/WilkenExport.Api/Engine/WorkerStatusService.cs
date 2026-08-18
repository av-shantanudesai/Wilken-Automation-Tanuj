namespace WilkenExport.Api.Engine;

/// <summary>
/// In-memory snapshot of what the single worker is currently doing,
/// exposed through the monitoring endpoint so operators never need to watch Wilken itself.
/// </summary>
public class WorkerStatusService
{
    private readonly object _lock = new();

    public string WorkerState { get; private set; } = "Idle";
    public string? CurrentRunId { get; private set; }
    public string? CurrentJobId { get; private set; }
    public string? CurrentJobLabel { get; private set; }
    public int? CurrentAttempt { get; private set; }
    public string? CurrentAction { get; private set; }
    public DateTime? CurrentJobStartedAt { get; private set; }
    public string? LastSuccessJobId { get; private set; }
    public DateTime? LastSuccessAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastErrorAt { get; private set; }

    public void SetIdle()
    {
        lock (_lock)
        {
            WorkerState = "Idle";
            CurrentRunId = null;
            CurrentJobId = null;
            CurrentJobLabel = null;
            CurrentAttempt = null;
            CurrentAction = null;
            CurrentJobStartedAt = null;
        }
    }

    public void SetJob(string runId, string jobId, string label, int attempt)
    {
        lock (_lock)
        {
            WorkerState = "Processing";
            CurrentRunId = runId;
            CurrentJobId = jobId;
            CurrentJobLabel = label;
            CurrentAttempt = attempt;
            CurrentJobStartedAt = DateTime.UtcNow;
        }
    }

    public void SetAction(string action)
    {
        lock (_lock) CurrentAction = action;
    }

    public void ReportSuccess(string jobId)
    {
        lock (_lock)
        {
            LastSuccessJobId = jobId;
            LastSuccessAt = DateTime.UtcNow;
        }
    }

    public void ReportError(string message)
    {
        lock (_lock)
        {
            LastError = message;
            LastErrorAt = DateTime.UtcNow;
        }
    }
}
