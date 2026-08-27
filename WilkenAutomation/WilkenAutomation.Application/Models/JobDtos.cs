using WilkenAutomation.Application.Enums;

namespace WilkenAutomation.Application.Models;

// ---------------------------------------------------------------------------
// Job-level wire DTOs. Property names/shapes intentionally match the existing
// Angular frontend (frontend/src/app/core/models.ts). Do not rename without
// checking the frontend first.
// ---------------------------------------------------------------------------

public class JobDto
{
    public string Id { get; set; } = default!;
    public string RunId { get; set; } = default!;
    public string Client { get; set; } = default!;
    public int FiscalYear { get; set; }
    public string Department { get; set; } = default!;
    public string DepartmentCode { get; set; } = default!;
    public string ExportDefinition { get; set; } = "";
    public string ExecutorType { get; set; } = "";
    public string Period { get; set; } = "";
    public string AccountingLaw { get; set; } = "";
    public string? SpoolId { get; set; }
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

public class ExportDefinitionDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Module { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Requires { get; set; } = new();
    public string Format { get; set; } = "";
}
