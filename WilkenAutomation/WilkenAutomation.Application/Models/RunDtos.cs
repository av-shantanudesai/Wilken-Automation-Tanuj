using WilkenAutomation.Application.Enums;

namespace WilkenAutomation.Application.Models;

// ---------------------------------------------------------------------------
// Run-level wire DTOs. Property names/shapes intentionally match the existing
// Angular frontend (frontend/src/app/core/models.ts). Do not rename without
// checking the frontend first.
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
    public static StatusCountsDto Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

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

/// <summary>Run creation request, shape-compatible with the existing frontend.</summary>
public class CreateRunRequestDto
{
    public List<string>? Clients { get; set; }
    public int? ClientCount { get; set; }
    public List<int>? Years { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public List<string>? Departments { get; set; }
    public List<string>? Periods { get; set; }
    public List<string>? ExportDefinitions { get; set; }
    public string? JobOrder { get; set; }
    public int? MaxAttempts { get; set; }
    public bool? EnableContentValidation { get; set; }
    public bool? EnableChecksum { get; set; }
    public SimulationConfig? Simulation { get; set; }
    public string? Notes { get; set; }
    public bool AutoStart { get; set; }
    /// <summary>Full path to Wilken CS/2 or the replica executable for this run.</summary>
    public string? WilkenExecutablePath { get; set; }
    /// <summary>Folder where original export files for this run are stored.</summary>
    public string? ExportRootDirectory { get; set; }
}
