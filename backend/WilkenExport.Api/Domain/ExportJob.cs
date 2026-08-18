namespace WilkenExport.Api.Domain;

public class ExportJob
{
    /// <summary>Deterministic unique job id, e.g. RUN-20260817-001-M001-2003-HR.</summary>
    public string Id { get; set; } = default!;

    public string RunId { get; set; } = default!;

    [System.Text.Json.Serialization.JsonIgnore]
    public ExportRun? Run { get; set; }

    public string Client { get; set; } = default!;
    public int FiscalYear { get; set; }

    /// <summary>Full department name: Handelsrecht (Commercial Law) or Steuerrecht (Tax Law).</summary>
    public string Department { get; set; } = default!;

    /// <summary>Short department code used in ids: HR / ST.</summary>
    public string DepartmentCode { get; set; } = default!;

    /// <summary>Position in the configured processing order.</summary>
    public int OrderIndex { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int AttemptCount { get; set; }

    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long? DurationMs { get; set; }

    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? Sha256 { get; set; }

    public ValidationOutcome ValidationOutcome { get; set; } = ValidationOutcome.NotValidated;
    public string? ValidationDetail { get; set; }

    /// <summary>Record count read from the validated export content (null if not validated).</summary>
    public int? RecordCount { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ScreenshotPath { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
