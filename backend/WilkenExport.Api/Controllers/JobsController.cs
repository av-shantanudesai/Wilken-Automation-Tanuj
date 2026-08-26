using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WilkenExport.Api.Api;
using WilkenExport.Api.Data;
using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Controllers;

[ApiController]
[Route("api")]
public class JobsController : ControllerBase
{
    private readonly ExportDbContext _db;

    public JobsController(ExportDbContext db) => _db = db;

    [HttpGet("jobs")]
    public async Task<ActionResult<PagedResult<ExportJob>>> List(
        [FromQuery] string? runId,
        [FromQuery] JobStatus? status,
        [FromQuery] string? client,
        [FromQuery] int? fiscalYear,
        [FromQuery] string? department,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = _db.Jobs.AsQueryable();
        if (!string.IsNullOrEmpty(runId)) query = query.Where(j => j.RunId == runId);
        if (status.HasValue) query = query.Where(j => j.Status == status.Value);
        if (!string.IsNullOrEmpty(client)) query = query.Where(j => j.Client == client);
        if (fiscalYear.HasValue) query = query.Where(j => j.FiscalYear == fiscalYear.Value);
        if (!string.IsNullOrEmpty(department)) query = query.Where(j => j.Department == department);

        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(j => j.OrderIndex)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<ExportJob> { Total = total, Page = page, PageSize = pageSize, Items = items });
    }

    [HttpGet("jobs/{id}")]
    public async Task<ActionResult<JobDetailDto>> Get(string id, CancellationToken ct)
    {
        var job = await _db.Jobs.FindAsync(new object[] { id }, ct);
        if (job == null) return NotFound();

        var logs = await _db.Logs
            .Where(l => l.JobId == id)
            .OrderBy(l => l.Timestamp)
            .ToListAsync(ct);

        return Ok(new JobDetailDto { Job = job, Logs = logs });
    }

    /// <summary>Requeue a single FAILED_FINAL (or invalid) job for reprocessing.</summary>
    [HttpPost("jobs/{id}/requeue")]
    public async Task<ActionResult<ExportJob>> Requeue(string id, CancellationToken ct)
    {
        var job = await _db.Jobs.FindAsync(new object[] { id }, ct);
        if (job == null) return NotFound();
        if (job.Status is JobStatus.Running)
            return Conflict("Job is currently running.");

        job.Status = JobStatus.Pending;
        job.AttemptCount = 0;
        job.UpdatedAt = DateTime.UtcNow;
        _db.Logs.Add(new JobLogEntry
        {
            Timestamp = DateTime.UtcNow,
            RunId = job.RunId,
            JobId = job.Id,
            Client = job.Client,
            FiscalYear = job.FiscalYear,
            Department = job.Department,
            Level = "INFO",
            Action = "REQUEUED",
            Message = "Job manually re-queued by operator."
        });
        await _db.SaveChangesAsync(ct);
        return Ok(job);
    }

    [HttpGet("logs")]
    public async Task<ActionResult<List<JobLogEntry>>> Logs(
        [FromQuery] string? runId,
        [FromQuery] string? jobId,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var query = _db.Logs.AsQueryable();
        if (!string.IsNullOrEmpty(runId)) query = query.Where(l => l.RunId == runId);
        if (!string.IsNullOrEmpty(jobId)) query = query.Where(l => l.JobId == jobId);

        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .ThenByDescending(l => l.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct);

        return Ok(items);
    }
}
