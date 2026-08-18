using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Interfaces;

public interface IRunRepository
{
    Task<AutomationRun> CreateWithJobsAsync(AutomationRun run, IEnumerable<ExportJob> jobs, CancellationToken ct);
    Task<AutomationRun?> GetByRunIdAsync(string runId, CancellationToken ct);
    Task<List<AutomationRun>> ListAsync(CancellationToken ct);
    Task<AutomationRun?> GetActiveRunAsync(CancellationToken ct);
    Task UpdateAsync(AutomationRun run, CancellationToken ct);

    /// <summary>Recomputes the run counter columns from the job table (source of truth).</summary>
    Task RefreshCountersAsync(string runId, CancellationToken ct);

    Task<StatusCountsDto> GetCountsAsync(string runId, CancellationToken ct);
    Task<int> CountRunsWithPrefixAsync(string prefix, CancellationToken ct);
}

public class JobFilter
{
    public string? RunId { get; set; }
    public JobStatus? Status { get; set; }
    public string? Client { get; set; }
    public int? FiscalYear { get; set; }
    public string? Department { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public interface IJobRepository
{
    Task<ExportJob?> GetByJobIdAsync(string jobId, CancellationToken ct);
    Task<(int Total, List<ExportJob> Items)> ListAsync(JobFilter filter, CancellationToken ct);
    Task<List<ExportJob>> GetAllForRunAsync(string runId, CancellationToken ct);
    Task<ExportJob?> GetNextEligibleAsync(string runId, CancellationToken ct);
    Task<ExportJob?> GetCurrentRunningAsync(CancellationToken ct);
    Task<List<ExportJob>> GetStaleRunningAsync(CancellationToken ct);
    Task<List<ExportJob>> GetSuccessfulAsync(string runId, CancellationToken ct);
    Task UpdateAsync(ExportJob job, CancellationToken ct);
    Task<(double? AverageMs, int CompletedCount)> GetRuntimeStatsAsync(string runId, CancellationToken ct);

    Task AddAttemptAsync(JobAttempt attempt, CancellationToken ct);
    Task UpdateAttemptAsync(JobAttempt attempt, CancellationToken ct);
    Task<List<JobAttempt>> GetAttemptsAsync(string jobId, CancellationToken ct);

    /// <summary>Persists job + attempt + run counters atomically (transaction).</summary>
    Task SaveJobTransitionAsync(ExportJob job, JobAttempt? attempt, string runId, CancellationToken ct);
}

public interface ILogRepository
{
    Task AddAsync(AutomationLog log, CancellationToken ct);
    Task<List<AutomationLog>> QueryAsync(string? runId, string? jobId, int limit, CancellationToken ct);
}
