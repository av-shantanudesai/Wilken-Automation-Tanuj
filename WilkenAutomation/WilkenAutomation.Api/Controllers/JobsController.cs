using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WilkenAutomation.Api.Auth;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[Authorize]
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
        if (runId is not null && await ForbidRunAsync(runId, ct) is { } denied) return denied;

        var (total, items) = await _jobs.ListAsync(new JobFilter
        {
            RunId = runId,
            UserId = User.GetRequiredUserId(),
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
        if (await ForbidRunAsync(current.RunId, ct) is not null) return NoContent();
        return current.ToDto();
    }

    [HttpGet("{jobId}")]
    public async Task<ActionResult<JobDetailDto>> Get(string jobId, CancellationToken ct)
    {
        var job = await _jobs.GetByJobIdAsync(jobId, ct);
        if (job is null) return NotFound();
        if (await ForbidRunAsync(job.RunId, ct) is { } denied) return denied;
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
        if (await ForbidRunAsync(job.RunId, ct) is { } denied) return denied;
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

        await _notifier.PublishAsync(SignalREvents.JobStatusChanged, job.ToDto(), ct, run?.UserId, job.RunId);
        await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId = job.RunId }, ct, run?.UserId, job.RunId);
        return job.ToDto();
    }

    private async Task<ActionResult?> ForbidRunAsync(string runId, CancellationToken ct)
    {
        var run = await _runs.GetByRunIdAsync(runId, ct);
        if (run is null || run.UserId != User.GetRequiredUserId()) return NotFound();
        return null;
    }
}

[ApiController]
[Authorize]
[Route("api/logs")]
public class LogsController : ControllerBase
{
    private readonly ILogRepository _logs;
    private readonly IRunRepository _runs;
    private readonly IJobRepository _jobs;

    public LogsController(ILogRepository logs, IRunRepository runs, IJobRepository jobs)
    {
        _logs = logs;
        _runs = runs;
        _jobs = jobs;
    }

    [HttpGet]
    public async Task<ActionResult<List<LogEntryDto>>> Query(
        [FromQuery] string? runId, [FromQuery] string? jobId, [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (runId is not null)
        {
            var run = await _runs.GetByRunIdAsync(runId, ct);
            if (run is null || run.UserId != User.GetRequiredUserId()) return NotFound();
        }
        else if (jobId is not null)
        {
            var job = await _jobs.GetByJobIdAsync(jobId, ct);
            if (job is null) return NotFound();
            var run = await _runs.GetByRunIdAsync(job.RunId, ct);
            if (run is null || run.UserId != User.GetRequiredUserId()) return NotFound();
        }
        else
        {
            return BadRequest(new { message = "runId or jobId is required." });
        }

        var logs = await _logs.QueryAsync(runId, jobId, limit, ct);
        return logs.Select(l => l.ToDto()).ToList();
    }
}
