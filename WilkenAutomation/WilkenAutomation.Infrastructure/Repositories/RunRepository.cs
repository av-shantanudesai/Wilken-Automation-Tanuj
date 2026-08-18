using Microsoft.EntityFrameworkCore;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Infrastructure.Repositories;

public class RunRepository : IRunRepository
{
    private readonly AutomationDbContext _db;

    public RunRepository(AutomationDbContext db)
    {
        _db = db;
    }

    public async Task<AutomationRun> CreateWithJobsAsync(AutomationRun run, IEnumerable<ExportJob> jobs, CancellationToken ct)
    {
        // Relational providers get a transaction; InMemory (tests) does not support them.
        var useTransaction = _db.Database.IsRelational();
        await using var tx = useTransaction ? await _db.Database.BeginTransactionAsync(ct) : null;

        _db.AutomationRuns.Add(run);
        _db.ExportJobs.AddRange(jobs);
        await _db.SaveChangesAsync(ct);

        if (tx is not null) await tx.CommitAsync(ct);
        return run;
    }

    public Task<AutomationRun?> GetByRunIdAsync(string runId, CancellationToken ct) =>
        _db.AutomationRuns.FirstOrDefaultAsync(r => r.RunId == runId, ct);

    public Task<List<AutomationRun>> ListAsync(CancellationToken ct) =>
        _db.AutomationRuns.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);

    public Task<AutomationRun?> GetActiveRunAsync(CancellationToken ct) =>
        _db.AutomationRuns
            .Where(r => r.Status == RunStatus.Running)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task UpdateAsync(AutomationRun run, CancellationToken ct)
    {
        run.UpdatedAt = DateTime.UtcNow;
        _db.AutomationRuns.Update(run);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RefreshCountersAsync(string runId, CancellationToken ct)
    {
        var run = await GetByRunIdAsync(runId, ct);
        if (run is null) return;

        var counts = await GetCountsAsync(runId, ct);
        run.TotalJobs = counts.Total;
        run.SuccessfulWithData = counts.SuccessWithData;
        run.SuccessfulEmpty = counts.SuccessEmpty;
        run.FailedJobs = counts.FailedFinal;
        run.PendingJobs = counts.Pending + counts.Retry;
        run.CompletedJobs = counts.Terminal;

        if (run.Status == RunStatus.Running && counts.Total > 0 && counts.Terminal == counts.Total)
        {
            run.Status = RunStatus.Completed;
            run.CompletedAt = DateTime.UtcNow;
        }

        await UpdateAsync(run, ct);
    }

    public async Task<StatusCountsDto> GetCountsAsync(string runId, CancellationToken ct)
    {
        var groups = await _db.ExportJobs
            .Where(j => j.RunId == runId)
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Of(JobStatus s) => groups.FirstOrDefault(g => g.Status == s)?.Count ?? 0;

        return new StatusCountsDto(
            groups.Sum(g => g.Count),
            Of(JobStatus.Pending),
            Of(JobStatus.Running),
            Of(JobStatus.Retry),
            Of(JobStatus.SuccessWithData),
            Of(JobStatus.SuccessEmpty),
            Of(JobStatus.FailedFinal));
    }

    public Task<int> CountRunsWithPrefixAsync(string prefix, CancellationToken ct) =>
        _db.AutomationRuns.CountAsync(r => r.RunId.StartsWith(prefix), ct);
}
