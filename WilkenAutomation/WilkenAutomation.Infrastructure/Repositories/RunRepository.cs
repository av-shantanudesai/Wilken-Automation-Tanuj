using Microsoft.EntityFrameworkCore;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Infrastructure.Repositories;

public class RunRepository : IRunRepository
{
    private readonly AutomationDbContext _db;

    public RunRepository(AutomationDbContext db)
    {
        _db = db;
    }

    public Task<AutomationRun> CreateWithJobsAsync(AutomationRun run, IEnumerable<ExportJob> jobs, CancellationToken ct) =>
        ExecuteInTransactionAsync(async () =>
        {
            _db.AutomationRuns.Add(run);
            _db.ExportJobs.AddRange(jobs);
            await _db.SaveChangesAsync(ct);
            return run;
        }, ct);

    private async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            if (!_db.Database.IsRelational())
                return await operation();

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var result = await operation();
            await tx.CommitAsync(ct);
            return result;
        });
    }

    public Task<AutomationRun?> GetByRunIdAsync(string runId, CancellationToken ct) =>
        _db.AutomationRuns.FirstOrDefaultAsync(r => r.RunId == runId, ct);

    public Task<List<AutomationRun>> ListAsync(CancellationToken ct, long userId) =>
        _db.AutomationRuns
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

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
        RunCounters.ApplyToRun(run, await GetCountsAsync(runId, ct));
        await UpdateAsync(run, ct);
    }

    public async Task<StatusCountsDto> GetCountsAsync(string runId, CancellationToken ct)
    {
        var groups = await _db.ExportJobs
            .Where(j => j.RunId == runId)
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return RunCounters.FromGroups(groups.Select(g => (g.Status, g.Count)));
    }

    public async Task<IReadOnlyDictionary<string, StatusCountsDto>> GetCountsForRunsAsync(
        IEnumerable<string> runIds, CancellationToken ct)
    {
        var ids = runIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<string, StatusCountsDto>();

        var groups = await _db.ExportJobs
            .Where(j => ids.Contains(j.RunId))
            .GroupBy(j => new { j.RunId, j.Status })
            .Select(g => new { g.Key.RunId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return ids.ToDictionary(
            id => id,
            id => RunCounters.FromGroups(groups.Where(g => g.RunId == id).Select(g => (g.Status, g.Count))));
    }

    public Task<int> CountRunsWithPrefixAsync(string prefix, CancellationToken ct) =>
        _db.AutomationRuns.CountAsync(r => r.RunId.StartsWith(prefix), ct);
}
