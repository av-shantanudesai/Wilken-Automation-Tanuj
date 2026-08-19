using Microsoft.EntityFrameworkCore;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Infrastructure.Repositories;

public class JobRepository : IJobRepository
{
    private readonly AutomationDbContext _db;

    public JobRepository(AutomationDbContext db)
    {
        _db = db;
    }

    public Task<ExportJob?> GetByJobIdAsync(string jobId, CancellationToken ct) =>
        _db.ExportJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);

    public async Task<(int Total, List<ExportJob> Items)> ListAsync(JobFilter filter, CancellationToken ct)
    {
        var query = _db.ExportJobs.AsQueryable();

        if (!string.IsNullOrEmpty(filter.RunId)) query = query.Where(j => j.RunId == filter.RunId);
        if (filter.UserId is not null)
        {
            var runIds = _db.AutomationRuns.Where(r => r.UserId == filter.UserId.Value).Select(r => r.RunId);
            query = query.Where(j => runIds.Contains(j.RunId));
        }
        if (filter.Status is not null) query = query.Where(j => j.Status == filter.Status);
        if (!string.IsNullOrEmpty(filter.Client)) query = query.Where(j => j.Client == filter.Client);
        if (filter.FiscalYear is not null) query = query.Where(j => j.FiscalYear == filter.FiscalYear);
        if (!string.IsNullOrEmpty(filter.Department)) query = query.Where(j => j.Department == filter.Department);

        var total = await query.CountAsync(ct);
        var pageSize = Math.Clamp(filter.PageSize, 1, 500);
        var page = Math.Max(1, filter.Page);
        var items = await query
            .OrderBy(j => j.OrderIndex)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (total, items);
    }

    public Task<List<ExportJob>> GetAllForRunAsync(string runId, CancellationToken ct) =>
        _db.ExportJobs.Where(j => j.RunId == runId).OrderBy(j => j.OrderIndex).ToListAsync(ct);

    public Task<ExportJob?> GetNextEligibleAsync(string runId, CancellationToken ct) =>
        _db.ExportJobs
            .Where(j => j.RunId == runId && (j.Status == JobStatus.Pending || j.Status == JobStatus.Retry))
            .OrderBy(j => j.OrderIndex)
            .FirstOrDefaultAsync(ct);

    public Task<ExportJob?> GetCurrentRunningAsync(CancellationToken ct) =>
        _db.ExportJobs.Where(j => j.Status == JobStatus.Running)
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefaultAsync(ct);

    public async Task<(ExportJob? LastSuccess, ExportJob? LastError)> GetStatusMarkersAsync(string runId, CancellationToken ct)
    {
        var lastSuccess = await _db.ExportJobs
            .Where(j => j.RunId == runId
                        && (j.Status == JobStatus.SuccessWithData || j.Status == JobStatus.SuccessEmpty)
                        && j.EndTime != null)
            .OrderByDescending(j => j.EndTime)
            .FirstOrDefaultAsync(ct);

        var lastError = await _db.ExportJobs
            .Where(j => j.RunId == runId && j.ErrorMessage != null)
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return (lastSuccess, lastError);
    }

    public Task<List<ExportJob>> GetStaleRunningAsync(CancellationToken ct) =>
        _db.ExportJobs.Where(j => j.Status == JobStatus.Running).ToListAsync(ct);

    public Task<List<ExportJob>> GetSuccessfulAsync(string runId, CancellationToken ct) =>
        _db.ExportJobs
            .Where(j => j.RunId == runId &&
                        (j.Status == JobStatus.SuccessWithData || j.Status == JobStatus.SuccessEmpty))
            .ToListAsync(ct);

    public async Task UpdateAsync(ExportJob job, CancellationToken ct)
    {
        _db.ExportJobs.Update(job);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(double? AverageMs, int CompletedCount)> GetRuntimeStatsAsync(string runId, CancellationToken ct)
    {
        var durations = await _db.ExportJobs
            .Where(j => j.RunId == runId && j.DurationMs != null &&
                        (j.Status == JobStatus.SuccessWithData || j.Status == JobStatus.SuccessEmpty))
            .Select(j => j.DurationMs!.Value)
            .ToListAsync(ct);

        return durations.Count == 0 ? (null, 0) : (durations.Average(), durations.Count);
    }

    public async Task AddAttemptAsync(JobAttempt attempt, CancellationToken ct)
    {
        _db.JobAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAttemptAsync(JobAttempt attempt, CancellationToken ct)
    {
        _db.JobAttempts.Update(attempt);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<JobAttempt>> GetAttemptsAsync(string jobId, CancellationToken ct) =>
        _db.JobAttempts.Where(a => a.JobId == jobId).OrderBy(a => a.AttemptNumber).ToListAsync(ct);

    public async Task SaveJobTransitionAsync(ExportJob job, JobAttempt? attempt, string runId, CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            if (_db.Database.IsRelational())
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                await PersistTransitionAsync(job, attempt, runId, ct);
                await tx.CommitAsync(ct);
            }
            else
            {
                await PersistTransitionAsync(job, attempt, runId, ct);
            }
        });
    }

    private async Task PersistTransitionAsync(ExportJob job, JobAttempt? attempt, string runId, CancellationToken ct)
    {
        _db.ExportJobs.Update(job);
        if (attempt is not null) _db.JobAttempts.Update(attempt);
        await _db.SaveChangesAsync(ct);
        await RefreshRunCountersInternalAsync(runId, ct);
    }

    private async Task RefreshRunCountersInternalAsync(string runId, CancellationToken ct)
    {
        var run = await _db.AutomationRuns.FirstOrDefaultAsync(r => r.RunId == runId, ct);
        if (run is null) return;

        var groups = await _db.ExportJobs
            .Where(j => j.RunId == runId)
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var counts = RunCounters.FromGroups(groups.Select(g => (g.Status, g.Count)));
        RunCounters.ApplyToRun(run, counts);

        await _db.SaveChangesAsync(ct);
    }
}
