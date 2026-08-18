using WilkenAutomation.Application.Enums;

namespace WilkenAutomation.Application.Models;

// ---------------------------------------------------------------------------
// Wire DTOs. Property names/shapes intentionally match the existing Angular
// frontend (frontend/src/app/core/models.ts). Do not rename without checking
// the frontend first.
// ---------------------------------------------------------------------------

public record StatusCountsDto(
    int Total,
    int Pending,
    int Running,
    int Retry,
    int SuccessWithData,
    int SuccessEmpty,
    int FailedFinal)
{
    public int Terminal => SuccessWithData + SuccessEmpty + FailedFinal;
    public int Open => Pending + Running + Retry;
}

public class RunSummaryDto
{
    public string Id { get; set; } = default!;
    public RunStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ExpectedJobCount { get; set; }
    public int GeneratedJobCount { get; set; }
    public bool JobCountDeviation { get; set; }
    public string? Notes { get; set; }
    public StatusCountsDto Counts { get; set; } = default!;
}

public class JobDto
{
    public string Id { get; set; } = default!;
    public string RunId { get; set; } = default!;
    public string Client { get; set; } = default!;
    public int FiscalYear { get; set; }
    public string Department { get; set; } = default!;
    public string DepartmentCode { get; set; } = default!;
    public int OrderIndex { get; set; }
    public JobStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long? DurationMs { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public ValidationStatus ValidationOutcome { get; set; }
    public string? ValidationDetail { get; set; }
    public int? RecordCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ScreenshotPath { get; set; }
    public string ApplicationState { get; set; } = "IDLE";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class RunStatusDto
{
    public string RunId { get; set; } = default!;
    public RunStatus RunStatus { get; set; }
    public int ExpectedJobCount { get; set; }
    public StatusCountsDto Counts { get; set; } = default!;
    public double ProgressPercent { get; set; }
    public double? AverageDurationMs { get; set; }
    public double? EstimatedRemainingMs { get; set; }
    public string WorkerState { get; set; } = "STOPPED";
    public string WilkenSessionStatus { get; set; } = "NotRunning";
    public string? CurrentJobId { get; set; }
    public string? CurrentJobLabel { get; set; }
    public int? CurrentAttempt { get; set; }
    public string? CurrentAction { get; set; }
    public DateTime? CurrentJobStartedAt { get; set; }
    public string? LastSuccessJobId { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }
}

public class LogEntryDto
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string RunId { get; set; } = default!;
    public string? JobId { get; set; }
    public string? Client { get; set; }
    public int? FiscalYear { get; set; }
    public string? Department { get; set; }
    public string Level { get; set; } = "INFO";
    public string Action { get; set; } = default!;
    public int? Attempt { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = "";
}

public class AuditReportDto
{
    public string RunId { get; set; } = default!;
    public RunStatus RunStatus { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ExpectedJobs { get; set; }
    public int AccountedJobs { get; set; }
    public bool Reconciles { get; set; }
    public StatusCountsDto Counts { get; set; } = default!;
    public double SuccessRatePercent { get; set; }
    public List<JobDto> FailedFinalJobs { get; set; } = new();
    public List<JobDto> Jobs { get; set; } = new();
}

public class PagedResultDto<T>
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<T> Items { get; set; } = new();
}

public class JobDetailDto
{
    public JobDto Job { get; set; } = default!;
    public List<LogEntryDto> Logs { get; set; } = new();
}

/// <summary>Run creation request, shape-compatible with the existing frontend.</summary>
public class CreateRunRequestDto
{
    public List<string>? Clients { get; set; }
    public int? ClientCount { get; set; }
    public List<int>? Years { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public List<string>? Departments { get; set; }
    public string? JobOrder { get; set; }
    public int? MaxAttempts { get; set; }
    public bool? EnableContentValidation { get; set; }
    public bool? EnableChecksum { get; set; }
    public SimulationConfig? Simulation { get; set; }
    public string? Notes { get; set; }
    public bool AutoStart { get; set; }
}

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

public static class DtoMapper
{
    public static JobDto ToDto(this ExportJob job) => new()
    {
        Id = job.JobId,
        RunId = job.RunId,
        Client = job.Client,
        FiscalYear = job.FiscalYear,
        Department = job.Department,
        DepartmentCode = job.DepartmentCode,
        OrderIndex = job.OrderIndex,
        Status = job.Status,
        AttemptCount = job.AttemptCount,
        StartTime = job.StartTime,
        EndTime = job.EndTime,
        DurationMs = job.DurationMs,
        FileName = job.FileName,
        FilePath = job.FilePath,
        FileSizeBytes = job.FileSize,
        Sha256 = job.Sha256,
        ValidationOutcome = job.ValidationStatus,
        ValidationDetail = job.ValidationDetail,
        RecordCount = job.RecordCount,
        ErrorCode = job.ErrorCode,
        ErrorMessage = job.ErrorMessage,
        ScreenshotPath = job.LastScreenshotPath,
        ApplicationState = job.ApplicationState,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt
    };

    public static LogEntryDto ToDto(this AutomationLog log) => new()
    {
        Id = log.Id,
        Timestamp = log.Timestamp,
        RunId = log.RunId,
        JobId = log.JobId,
        Client = log.Client,
        FiscalYear = log.FiscalYear,
        Department = log.Department,
        Level = log.Level,
        Action = log.Action,
        Attempt = log.Attempt,
        DurationMs = log.DurationMs,
        ErrorCode = log.ErrorCode,
        Message = log.Message
    };

    public static RunSummaryDto ToSummaryDto(this AutomationRun run, StatusCountsDto counts) => new()
    {
        Id = run.RunId,
        Status = run.Status,
        CreatedAt = run.CreatedAt,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        ExpectedJobCount = run.ExpectedJobs,
        GeneratedJobCount = run.TotalJobs,
        JobCountDeviation = run.ExpectedJobs != run.TotalJobs,
        Notes = run.Notes,
        Counts = counts
    };
}
