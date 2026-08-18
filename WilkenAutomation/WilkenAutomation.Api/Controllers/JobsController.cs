using Microsoft.AspNetCore.Mvc;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private readonly IJobRepository _jobs;
    private readonly IRunRepository _runs;
    private readonly ILogRepository _logs;
    private readonly IRealtimeNotifier _notifier;

    public JobsController(IJobRepository jobs, IRunRepository runs, ILogRepository logs, IRealtimeNotifier notifier)
    {
        _jobs = jobs;
        _runs = runs;
        _logs = logs;
        _notifier = notifier;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<JobDto>>> List(
        [FromQuery] string? runId, [FromQuery] JobStatus? status, [FromQuery] string? client,
        [FromQuery] int? fiscalYear, [FromQuery] string? department,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var (total, items) = await _jobs.ListAsync(new JobFilter
        {
            RunId = runId,
            Status = status,
            Client = client,
            FiscalYear = fiscalYear,
            Department = department,
            Page = page,
            PageSize = pageSize
        }, ct);

        return new PagedResultDto<JobDto>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items.Select(j => j.ToDto()).ToList()
        };
    }

    [HttpGet("current")]
    public async Task<ActionResult<JobDto?>> Current(CancellationToken ct)
    {
        var current = await _jobs.GetCurrentRunningAsync(ct);
        if (current is null) return NoContent();
        return current.ToDto();
    }

    [HttpGet("{jobId}")]
    public async Task<ActionResult<JobDetailDto>> Get(string jobId, CancellationToken ct)
    {
        var job = await _jobs.GetByJobIdAsync(jobId, ct);
        if (job is null) return NotFound();
        var logs = await _logs.QueryAsync(null, jobId, 200, ct);
        return new JobDetailDto
        {
            Job = job.ToDto(),
            Logs = logs.Select(l => l.ToDto()).ToList()
        };
    }

    [HttpPost("{jobId}/requeue")]
    [HttpPost("{jobId}/retry")]
    public async Task<ActionResult<JobDto>> Requeue(string jobId, CancellationToken ct)
    {
        var job = await _jobs.GetByJobIdAsync(jobId, ct);
        if (job is null) return NotFound();
        if (!JobStateMachine.CanTransition(job.Status, JobStatus.Pending))
            return Conflict(new { message = $"Job in status {job.Status} cannot be re-queued." });

        job.Status = JobStatus.Pending;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _jobs.UpdateAsync(job, ct);

        var run = await _runs.GetByRunIdAsync(job.RunId, ct);
        if (run is not null && run.Status == RunStatus.Completed)
        {
            run.Status = RunStatus.Running;
            run.CompletedAt = null;
            await _runs.UpdateAsync(run, ct);
        }
        await _runs.RefreshCountersAsync(job.RunId, ct);

        await _notifier.PublishAsync(SignalREvents.JobStatusChanged, job.ToDto(), ct);
        await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId = job.RunId }, ct);
        return job.ToDto();
    }
}

[ApiController]
[Route("api/logs")]
public class LogsController : ControllerBase
{
    private readonly ILogRepository _logs;

    public LogsController(ILogRepository logs)
    {
        _logs = logs;
    }

    [HttpGet]
    public async Task<ActionResult<List<LogEntryDto>>> Query(
        [FromQuery] string? runId, [FromQuery] string? jobId, [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var logs = await _logs.QueryAsync(runId, jobId, limit, ct);
        return logs.Select(l => l.ToDto()).ToList();
    }
}
