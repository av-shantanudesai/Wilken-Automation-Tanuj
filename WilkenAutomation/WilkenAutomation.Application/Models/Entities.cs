using WilkenAutomation.Application.Enums;

namespace WilkenAutomation.Application.Models;

public class AutomationRun
{
    public long Id { get; set; }

    /// <summary>Business id, e.g. RUN-20260818-001.</summary>
    public string RunId { get; set; } = default!;

    public RunStatus Status { get; set; } = RunStatus.Created;

    public int ExpectedJobs { get; set; }
    public int TotalJobs { get; set; }
    public int CompletedJobs { get; set; }
    public int SuccessfulWithData { get; set; }
    public int SuccessfulEmpty { get; set; }
    public int FailedJobs { get; set; }
    public int PendingJobs { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Immutable snapshot of the effective configuration (JSON).</summary>
    public string ConfigJson { get; set; } = "{}";

    public string? Notes { get; set; }
}

public class ExportJob
{
    public long Id { get; set; }

    /// <summary>Business id, e.g. RUN-20260818-001-M001-2003-HR.</summary>
    public string JobId { get; set; } = default!;

    public string RunId { get; set; } = default!;

    public string Client { get; set; } = default!;
    public int FiscalYear { get; set; }
    public string Department { get; set; } = default!;
    public string DepartmentCode { get; set; } = default!;
    public int OrderIndex { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int AttemptCount { get; set; }

    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long? DurationMs { get; set; }

    /// <summary>Fine-grained UPPER_SNAKE state, e.g. WAITING_FOR_REPORT.</summary>
    public string ApplicationState { get; set; } = "IDLE";

    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Sha256 { get; set; }

    public ValidationStatus ValidationStatus { get; set; } = ValidationStatus.NotValidated;
    public string? ValidationDetail { get; set; }
    public int? RecordCount { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LastScreenshotPath { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class JobAttempt
{
    public long Id { get; set; }
    public string JobId { get; set; } = default!;
    public int AttemptNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>Running | Succeeded | Failed | Interrupted</summary>
    public string Status { get; set; } = "Running";

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ScreenshotPath { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AutomationLog
{
    public long Id { get; set; }
    public string RunId { get; set; } = default!;
    public string? JobId { get; set; }
    public string? Client { get; set; }
    public int? FiscalYear { get; set; }
    public string? Department { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>INFO | WARN | ERROR</summary>
    public string Level { get; set; } = "INFO";

    public string Action { get; set; } = default!;
    public string Message { get; set; } = "";
    public string? ApplicationState { get; set; }
    public int? Attempt { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? AdditionalData { get; set; }
}
