using Microsoft.EntityFrameworkCore;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Infrastructure.Repositories;

public class LogRepository : ILogRepository
{
    private readonly AutomationDbContext _db;

    public LogRepository(AutomationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AutomationLog log, CancellationToken ct)
    {
        _db.AutomationLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<AutomationLog>> QueryAsync(string? runId, string? jobId, int limit, CancellationToken ct)
    {
        var query = _db.AutomationLogs.AsQueryable();
        if (!string.IsNullOrEmpty(runId)) query = query.Where(l => l.RunId == runId);
        if (!string.IsNullOrEmpty(jobId)) query = query.Where(l => l.JobId == jobId);
        return query
            .OrderByDescending(l => l.Timestamp)
            .ThenByDescending(l => l.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct);
    }
}
