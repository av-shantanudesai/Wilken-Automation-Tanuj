using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Worker.Worker;

/// <summary>Thread-safe snapshot of what the worker agent is currently doing.</summary>
public class WorkerState
{
    private readonly object _lock = new();

    public WorkerStatus Status { get; private set; } = WorkerStatus.Stopped;
    public WilkenSessionStatus SessionStatus { get; private set; } = WilkenSessionStatus.NotRunning;
    public AutomationMode Mode { get; set; } = AutomationMode.Mock;

    private string? _runId;
    private long? _ownerUserId;
    private string? _jobId;
    private string? _jobLabel;
    private int? _attempt;
    private string? _action;
    private DateTime? _jobStartedAt;
    private string? _lastSuccessJobId;
    private DateTime? _lastSuccessAt;
    private string? _lastError;
    private DateTime? _lastErrorAt;

    public void SetStatus(WorkerStatus status)
    {
        lock (_lock) Status = status;
    }

    public void SetSession(WilkenSessionStatus session)
    {
        lock (_lock) SessionStatus = session;
    }

    public void SetCurrentJob(ExportJob? job, long? ownerUserId = null)
    {
        lock (_lock)
        {
            if (job is null)
            {
                _runId = null;
                _ownerUserId = null;
                _jobId = null;
                _jobLabel = null;
                _attempt = null;
                _jobStartedAt = null;
                _action = null;
                return;
            }

            _runId = job.RunId;
            if (ownerUserId is > 0) _ownerUserId = ownerUserId;
            _jobId = job.JobId;
            _jobLabel = $"Client {job.Client} / {job.FiscalYear} / {job.Department}";
            _attempt = job.AttemptCount;
            _jobStartedAt = job.StartTime;
        }
    }

    public void SetAction(string action)
    {
        lock (_lock) _action = action;
    }

    public void ReportSuccess(string jobId)
    {
        lock (_lock)
        {
            _lastSuccessJobId = jobId;
            _lastSuccessAt = DateTime.UtcNow;
        }
    }

    public void ReportError(string message)
    {
        lock (_lock)
        {
            _lastError = message;
            _lastErrorAt = DateTime.UtcNow;
        }
    }

    public long? OwnerUserId
    {
        get { lock (_lock) return _ownerUserId; }
    }

    public WorkerStatusDto Snapshot()
    {
        lock (_lock)
        {
            return new WorkerStatusDto
            {
                WorkerState = Status.ToWireName(),
                WilkenSessionStatus = SessionStatus.ToString(),
                AutomationMode = Mode.ToString(),
                CurrentRunId = _runId,
                CurrentJobId = _jobId,
                CurrentJobLabel = _jobLabel,
                CurrentAttempt = _attempt,
                CurrentAction = _action,
                CurrentJobStartedAt = _jobStartedAt,
                RuntimeSeconds = _jobStartedAt is null ? null : (DateTime.UtcNow - _jobStartedAt.Value).TotalSeconds,
                LastSuccessJobId = _lastSuccessJobId,
                LastSuccessAt = _lastSuccessAt,
                LastError = _lastError,
                LastErrorAt = _lastErrorAt,
                HeartbeatAt = DateTime.UtcNow
            };
        }
    }
}
