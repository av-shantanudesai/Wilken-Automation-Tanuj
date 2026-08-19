using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WilkenAutomation.Api.Auth;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/runs")]
public class RunsController : ControllerBase
{
    private readonly IRunRepository _runs;
    private readonly IJobRepository _jobs;
    private readonly JobGeneratorService _generator;
    private readonly RunStatisticsService _statistics;
    private readonly WorkerStatusRegistry _registry;
    private readonly IRealtimeNotifier _notifier;

    public RunsController(
        IRunRepository runs,
        IJobRepository jobs,
        JobGeneratorService generator,
        RunStatisticsService statistics,
        WorkerStatusRegistry registry,
        IRealtimeNotifier notifier)
    {
        _runs = runs;
        _jobs = jobs;
        _generator = generator;
        _statistics = statistics;
        _registry = registry;
        _notifier = notifier;
    }

    [HttpGet]
    public async Task<ActionResult<List<RunSummaryDto>>> List(CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();
        var runs = await _runs.ListAsync(ct, userId);
        var counts = await _runs.GetCountsForRunsAsync(runs.Select(r => r.RunId), ct);
        return runs.Select(run => run.ToSummaryDto(counts.GetValueOrDefault(run.RunId, StatusCountsDto.Empty))).ToList();
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<RunSummaryDto>> Get(string runId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, ct);
        if (run is null) return NotFound();
        return run.ToSummaryDto(await _runs.GetCountsAsync(runId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<RunSummaryDto>> Create([FromBody] CreateRunRequestDto request, CancellationToken ct)
    {
        var config = _generator.BuildConfig(request);
        var run = await _generator.GenerateRunAsync(config, request.Notes, request.AutoStart, ct, User.GetRequiredUserId());
        await _notifier.PublishAsync(SignalREvents.RunsChanged, new { runId = run.RunId }, ct, run.UserId);
        return run.ToSummaryDto(await _runs.GetCountsAsync(run.RunId, ct));
    }

    [HttpPost("{runId}/start")]
    public async Task<ActionResult<RunSummaryDto>> Start(string runId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, ct);
        if (run is null) return NotFound();
        if (run.Status is RunStatus.Created or RunStatus.Paused or RunStatus.Completed)
        {
            run.Status = RunStatus.Running;
            run.StartedAt ??= DateTime.UtcNow;
            run.CompletedAt = null;
            await _runs.UpdateAsync(run, ct);
            await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId }, ct, run.UserId);
        }
        return run.ToSummaryDto(await _runs.GetCountsAsync(runId, ct));
    }

    [HttpPost("{runId}/pause")]
    public async Task<ActionResult<RunSummaryDto>> Pause(string runId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, ct);
        if (run is null) return NotFound();
        if (run.Status == RunStatus.Running)
        {
            run.Status = RunStatus.Paused;
            await _runs.UpdateAsync(run, ct);
            await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId }, ct, run.UserId);
        }
        return run.ToSummaryDto(await _runs.GetCountsAsync(runId, ct));
    }

    [HttpPost("{runId}/retry-failed")]
    public async Task<ActionResult<RunSummaryDto>> RetryFailed(string runId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, ct);
        if (run is null) return NotFound();

        var (_, failed) = await _jobs.ListAsync(
            new JobFilter { RunId = runId, Status = JobStatus.FailedFinal, PageSize = 500 }, ct);
        foreach (var job in failed)
        {
            JobStateMachine.EnsureTransition(job.Status, JobStatus.Pending);
            job.Status = JobStatus.Pending;
            job.ErrorCode = null;
            job.ErrorMessage = null;
            job.UpdatedAt = DateTime.UtcNow;
            await _jobs.UpdateAsync(job, ct);
        }

        if (failed.Count > 0)
        {
            if (run.Status == RunStatus.Completed)
            {
                run.Status = RunStatus.Running;
                run.CompletedAt = null;
                await _runs.UpdateAsync(run, ct);
            }
            await _runs.RefreshCountersAsync(runId, ct);
            await _notifier.PublishAsync(SignalREvents.RunProgressChanged, new { runId }, ct, run.UserId);
        }

        return run.ToSummaryDto(await _runs.GetCountsAsync(runId, ct));
    }

    [HttpGet("{runId}/status")]
    [HttpGet("{runId}/summary")]
    public async Task<ActionResult<RunStatusDto>> Status(string runId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, ct);
        if (run is null) return NotFound();

        var counts = await _runs.GetCountsAsync(runId, ct);
        var (progress, averageMs, remainingMs) = await _statistics.ComputeAsync(runId, counts, ct);
        var worker = _registry.Snapshot();

        var current = await _jobs.GetCurrentRunningAsync(ct);
        if (current is not null && current.RunId != runId) current = null;

        var (lastSuccess, lastError) = await _jobs.GetStatusMarkersAsync(runId, ct);

        return new RunStatusDto
        {
            RunId = runId,
            RunStatus = run.Status,
            ExpectedJobCount = run.ExpectedJobs,
            Counts = counts,
            ProgressPercent = progress,
            AverageDurationMs = averageMs,
            EstimatedRemainingMs = remainingMs,
            WorkerState = worker.WorkerState,
            WilkenSessionStatus = worker.WilkenSessionStatus,
            CurrentJobId = current?.JobId,
            CurrentJobLabel = current is null
                ? null
                : $"Client {current.Client} / {current.FiscalYear} / {current.Department}",
            CurrentAttempt = current?.AttemptCount,
            CurrentAction = current?.ApplicationState ?? worker.CurrentAction,
            CurrentJobStartedAt = current?.StartTime,
            LastSuccessJobId = lastSuccess?.JobId,
            LastSuccessAt = lastSuccess?.EndTime,
            LastError = lastError?.ErrorMessage,
            LastErrorAt = lastError?.UpdatedAt
        };
    }

    [HttpGet("{runId}/jobs")]
    public async Task<ActionResult<PagedResultDto<JobDto>>> Jobs(
        string runId, [FromQuery] JobStatus? status, [FromQuery] string? client,
        [FromQuery] int? fiscalYear, [FromQuery] string? department,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (await OwnedRunAsync(runId, ct) is null) return NotFound();
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

    [HttpGet("{runId}/audit")]
    public async Task<ActionResult<AuditReportDto>> Audit(string runId, CancellationToken ct)
    {
        var report = await BuildAuditAsync(runId, ct);
        return report is null ? NotFound() : report;
    }

    [HttpGet("{runId}/audit.csv")]
    public async Task<IActionResult> AuditCsv(string runId, CancellationToken ct)
    {
        var report = await BuildAuditAsync(runId, ct);
        if (report is null) return NotFound();

        var sb = new StringBuilder();
        sb.AppendLine("RunId;JobId;Client;FiscalYear;Department;FinalStatus;FileName;FilePath;FileSizeBytes;Sha256;ExportTime;RuntimeMs;AttemptCount;ValidationResult;ErrorMessage");
        foreach (var job in report.Jobs)
        {
            sb.AppendLine(string.Join(';',
                report.RunId, job.Id, job.Client, job.FiscalYear, job.Department, job.Status,
                Csv(job.FileName), Csv(job.FilePath), job.FileSizeBytes, job.Sha256,
                job.EndTime?.ToString("O"), job.DurationMs, job.AttemptCount,
                job.ValidationOutcome, Csv(job.ErrorMessage)));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"{runId}_audit.csv");
    }

    private async Task<AuditReportDto?> BuildAuditAsync(string runId, CancellationToken ct)
    {
        var run = await OwnedRunAsync(runId, ct);
        if (run is null) return null;

        var counts = await _runs.GetCountsAsync(runId, ct);
        var jobs = (await _jobs.GetAllForRunAsync(runId, ct)).Select(j => j.ToDto()).ToList();
        var accounted = counts.Terminal + counts.Open;

        return new AuditReportDto
        {
            RunId = runId,
            RunStatus = run.Status,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            ExpectedJobs = run.ExpectedJobs,
            AccountedJobs = accounted,
            Reconciles = accounted == run.ExpectedJobs,
            Counts = counts,
            SuccessRatePercent = counts.Total == 0
                ? 0
                : Math.Round((counts.SuccessWithData + counts.SuccessEmpty) * 100.0 / counts.Total, 2),
            FailedFinalJobs = jobs.Where(j => j.Status == JobStatus.FailedFinal).ToList(),
            Jobs = jobs
        };
    }

    private async Task<AutomationRun?> OwnedRunAsync(string runId, CancellationToken ct)
    {
        var run = await _runs.GetByRunIdAsync(runId, ct);
        if (run is null || run.UserId != User.GetRequiredUserId()) return null;
        return run;
    }

    private static string Csv(string? value) =>
        value is null ? "" : value.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
}
