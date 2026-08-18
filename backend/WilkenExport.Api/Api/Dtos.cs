using WilkenExport.Api.Configuration;
using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Api;

public class CreateRunRequest
{
    /// <summary>Explicit client list; if null/empty, ClientCount generates 001..NNN.</summary>
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
    public SimulationOptions? Simulation { get; set; }
    public string? Notes { get; set; }

    /// <summary>Start processing immediately after generation.</summary>
    public bool AutoStart { get; set; }
}

public record StatusCounts(
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
    public StatusCounts Counts { get; set; } = default!;
}

public class RunStatusDto
{
    public string RunId { get; set; } = default!;
    public RunStatus RunStatus { get; set; }
    public int ExpectedJobCount { get; set; }
    public StatusCounts Counts { get; set; } = default!;
    public double ProgressPercent { get; set; }
    public double? AverageDurationMs { get; set; }
    public double? EstimatedRemainingMs { get; set; }

    public string WorkerState { get; set; } = default!;
    public string WilkenSessionStatus { get; set; } = default!;
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

public class AuditReportDto
{
    public string RunId { get; set; } = default!;
    public RunStatus RunStatus { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ExpectedJobs { get; set; }
    public int AccountedJobs { get; set; }
    public bool Reconciles { get; set; }
    public StatusCounts Counts { get; set; } = default!;
    public double SuccessRatePercent { get; set; }
    public List<ExportJob> FailedFinalJobs { get; set; } = new();
    public List<ExportJob> Jobs { get; set; } = new();
}

public class PagedResult<T>
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<T> Items { get; set; } = new();
}

public class JobDetailDto
{
    public ExportJob Job { get; set; } = default!;
    public List<JobLogEntry> Logs { get; set; } = new();
}
