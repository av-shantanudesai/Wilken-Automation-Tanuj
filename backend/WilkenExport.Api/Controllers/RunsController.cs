using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WilkenExport.Api.Adapters;
using WilkenExport.Api.Api;
using WilkenExport.Api.Configuration;
using WilkenExport.Api.Data;
using WilkenExport.Api.Domain;
using WilkenExport.Api.Engine;

namespace WilkenExport.Api.Controllers;

[ApiController]
[Route("api/runs")]
public class RunsController : ControllerBase
{
    private readonly ExportDbContext _db;
    private readonly JobGenerator _generator;
    private readonly RunVerificationService _verification;
    private readonly WorkerStatusService _workerStatus;
    private readonly IWilkenAdapter _wilken;
    private readonly ExportOptions _defaults;

    public RunsController(
        ExportDbContext db,
        JobGenerator generator,
        RunVerificationService verification,
        WorkerStatusService workerStatus,
        IWilkenAdapter wilken,
        IOptions<ExportOptions> defaults)
    {
        _db = db;
        _generator = generator;
        _verification = verification;
        _workerStatus = workerStatus;
        _wilken = wilken;
        _defaults = defaults.Value;
    }

    [HttpPost]
    public async Task<ActionResult<RunSummaryDto>> Create([FromBody] CreateRunRequest request, CancellationToken ct)
    {
        var config = BuildConfig(request);
        if (config.ExpectedJobCount == 0)
            return BadRequest("Configuration yields zero jobs - check clients, years and departments.");

        var run = await _generator.GenerateRunAsync(config, request.Notes, ct);

        if (request.AutoStart)
        {
            run.Status = RunStatus.Running;
            run.StartedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(await ToSummaryAsync(run, ct));
    }

    [HttpGet]
    public async Task<ActionResult<List<RunSummaryDto>>> List(CancellationToken ct)
    {
        var runs = await _db.Runs.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var result = new List<RunSummaryDto>();
        foreach (var run in runs) result.Add(await ToSummaryAsync(run, ct));
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RunSummaryDto>> Get(string id, CancellationToken ct)
    {
        var run = await _db.Runs.FindAsync(new object[] { id }, ct);
        if (run == null) return NotFound();
        return Ok(await ToSummaryAsync(run, ct));
    }

    /// <summary>Start or resume a run. Completed jobs are re-verified before being skipped.</summary>
    [HttpPost("{id}/start")]
    public async Task<ActionResult<RunSummaryDto>> Start(string id, CancellationToken ct)
    {
        var run = await _db.Runs.FindAsync(new object[] { id }, ct);
        if (run == null) return NotFound();

        var requeued = await _verification.VerifyCompletedJobsAsync(run, ct);

        run.Status = RunStatus.Running;
        run.StartedAt ??= DateTime.UtcNow;
        run.CompletedAt = null;
        _db.Logs.Add(new JobLogEntry
        {
            Timestamp = DateTime.UtcNow,
            RunId = run.Id,
            Level = "INFO",
            Action = "RUN_STARTED",
            Message = requeued > 0
                ? $"Run started/resumed; {requeued} previously completed job(s) failed re-verification and were re-queued."
                : "Run started/resumed; all previously completed jobs verified."
        });
        await _db.SaveChangesAsync(ct);
        return Ok(await ToSummaryAsync(run, ct));
    }

    [HttpPost("{id}/pause")]
    public async Task<ActionResult<RunSummaryDto>> Pause(string id, CancellationToken ct)
    {
        var run = await _db.Runs.FindAsync(new object[] { id }, ct);
        if (run == null) return NotFound();

        run.Status = RunStatus.Paused;
        _db.Logs.Add(new JobLogEntry
        {
            Timestamp = DateTime.UtcNow,
            RunId = run.Id,
            Level = "INFO",
            Action = "RUN_PAUSED",
            Message = "Run paused by operator; the current job finishes, then the worker idles."
        });
        await _db.SaveChangesAsync(ct);
        return Ok(await ToSummaryAsync(run, ct));
    }

    /// <summary>Targeted reprocessing of FAILED_FINAL jobs.</summary>
    [HttpPost("{id}/retry-failed")]
    public async Task<ActionResult<RunSummaryDto>> RetryFailed(string id, CancellationToken ct)
    {
        var run = await _db.Runs.FindAsync(new object[] { id }, ct);
        if (run == null) return NotFound();

        var failed = await _db.Jobs
            .Where(j => j.RunId == id && j.Status == JobStatus.FailedFinal)
            .ToListAsync(ct);

        foreach (var job in failed)
        {
            job.Status = JobStatus.Pending;
            job.AttemptCount = 0;
            job.UpdatedAt = DateTime.UtcNow;
            _db.Logs.Add(new JobLogEntry
            {
                Timestamp = DateTime.UtcNow,
                RunId = id,
                JobId = job.Id,
                Client = job.Client,
                FiscalYear = job.FiscalYear,
                Department = job.Department,
                Level = "INFO",
                Action = "REQUEUED",
                Message = "FAILED_FINAL job re-queued for targeted reprocessing."
            });
        }

        if (failed.Count > 0 && run.Status == RunStatus.Completed)
        {
            run.Status = RunStatus.Running;
            run.CompletedAt = null;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(await ToSummaryAsync(run, ct));
    }

    [HttpGet("{id}/status")]
    public async Task<ActionResult<RunStatusDto>> Status(string id, CancellationToken ct)
    {
        var run = await _db.Runs.FindAsync(new object[] { id }, ct);
        if (run == null) return NotFound();

        var counts = await CountsAsync(id, ct);

        var successDurations = await _db.Jobs
            .Where(j => j.RunId == id && j.DurationMs != null &&
                        (j.Status == JobStatus.SuccessWithData || j.Status == JobStatus.SuccessEmpty))
            .Select(j => (double)j.DurationMs!)
            .ToListAsync(ct);

        double? avg = successDurations.Count > 0 ? successDurations.Average() : null;

        return Ok(new RunStatusDto
        {
            RunId = run.Id,
            RunStatus = run.Status,
            ExpectedJobCount = run.ExpectedJobCount,
            Counts = counts,
            ProgressPercent = counts.Total == 0 ? 0 : Math.Round(100.0 * counts.Terminal / counts.Total, 2),
            AverageDurationMs = avg,
            EstimatedRemainingMs = avg.HasValue ? avg.Value * counts.Open : null,
            WorkerState = _workerStatus.WorkerState,
            WilkenSessionStatus = _wilken.SessionStatus,
            CurrentJobId = _workerStatus.CurrentJobId,
            CurrentJobLabel = _workerStatus.CurrentJobLabel,
            CurrentAttempt = _workerStatus.CurrentAttempt,
            CurrentAction = _workerStatus.CurrentAction,
            CurrentJobStartedAt = _workerStatus.CurrentJobStartedAt,
            LastSuccessJobId = _workerStatus.LastSuccessJobId,
            LastSuccessAt = _workerStatus.LastSuccessAt,
            LastError = _workerStatus.LastError,
            LastErrorAt = _workerStatus.LastErrorAt
        });
    }

    [HttpGet("{id}/audit")]
    public async Task<ActionResult<AuditReportDto>> Audit(string id, CancellationToken ct)
    {
        var run = await _db.Runs.FindAsync(new object[] { id }, ct);
        if (run == null) return NotFound();

        var jobs = await _db.Jobs.Where(j => j.RunId == id).OrderBy(j => j.OrderIndex).ToListAsync(ct);
        var counts = await CountsAsync(id, ct);

        return Ok(new AuditReportDto
        {
            RunId = run.Id,
            RunStatus = run.Status,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            ExpectedJobs = run.ExpectedJobCount,
            AccountedJobs = counts.Total,
            Reconciles = counts.Total == run.ExpectedJobCount,
            Counts = counts,
            SuccessRatePercent = counts.Total == 0 ? 0 :
                Math.Round(100.0 * (counts.SuccessWithData + counts.SuccessEmpty) / counts.Total, 2),
            FailedFinalJobs = jobs.Where(j => j.Status == JobStatus.FailedFinal).ToList(),
            Jobs = jobs
        });
    }

    [HttpGet("{id}/audit.csv")]
    public async Task<IActionResult> AuditCsv(string id, CancellationToken ct)
    {
        var run = await _db.Runs.FindAsync(new object[] { id }, ct);
        if (run == null) return NotFound();

        var jobs = await _db.Jobs.Where(j => j.RunId == id).OrderBy(j => j.OrderIndex).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("RunId;JobId;Client;FiscalYear;Department;FinalStatus;AttemptCount;FileName;FilePath;FileSizeBytes;Sha256;StartTime;EndTime;DurationMs;ValidationOutcome;ValidationDetail;RecordCount;ErrorCode;ErrorMessage");
        foreach (var j in jobs)
        {
            sb.AppendLine(string.Join(';',
                j.RunId, j.Id, j.Client, j.FiscalYear, j.Department, j.Status, j.AttemptCount,
                Csv(j.FileName), Csv(j.FilePath), j.FileSizeBytes, j.Sha256,
                j.StartTime?.ToString("O"), j.EndTime?.ToString("O"), j.DurationMs,
                j.ValidationOutcome, Csv(j.ValidationDetail), j.RecordCount,
                j.ErrorCode, Csv(j.ErrorMessage)));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"{id}_audit.csv");
    }

    private static string Csv(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');

    private async Task<StatusCounts> CountsAsync(string runId, CancellationToken ct)
    {
        var grouped = await _db.Jobs.Where(j => j.RunId == runId)
            .GroupBy(j => j.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Of(JobStatus s) => grouped.FirstOrDefault(g => g.Key == s)?.Count ?? 0;

        return new StatusCounts(
            grouped.Sum(g => g.Count),
            Of(JobStatus.Pending),
            Of(JobStatus.Running),
            Of(JobStatus.Retry),
            Of(JobStatus.SuccessWithData),
            Of(JobStatus.SuccessEmpty),
            Of(JobStatus.FailedFinal));
    }

    private async Task<RunSummaryDto> ToSummaryAsync(ExportRun run, CancellationToken ct) => new()
    {
        Id = run.Id,
        Status = run.Status,
        CreatedAt = run.CreatedAt,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        ExpectedJobCount = run.ExpectedJobCount,
        GeneratedJobCount = run.GeneratedJobCount,
        JobCountDeviation = run.ExpectedJobCount != run.GeneratedJobCount,
        Notes = run.Notes,
        Counts = await CountsAsync(run.Id, ct)
    };

    private RunConfig BuildConfig(CreateRunRequest request)
    {
        var clients = request.Clients is { Count: > 0 }
            ? request.Clients
            : Enumerable.Range(1, request.ClientCount ?? (_defaults.Clients.Count > 0 ? _defaults.Clients.Count : _defaults.ClientCount))
                .Select(i => i.ToString("D3")).ToList();

        List<int> years;
        if (request.Years is { Count: > 0 }) years = request.Years;
        else if (request.YearFrom.HasValue && request.YearTo.HasValue)
            years = Enumerable.Range(request.YearFrom.Value, request.YearTo.Value - request.YearFrom.Value + 1).ToList();
        else if (_defaults.Years.Count > 0) years = _defaults.Years;
        else years = Enumerable.Range(_defaults.YearFrom, _defaults.YearTo - _defaults.YearFrom + 1).ToList();

        var departments = request.Departments is { Count: > 0 } ? request.Departments
            : _defaults.Departments.Count > 0 ? _defaults.Departments
            : new List<string> { "Handelsrecht", "Steuerrecht" };

        return new RunConfig
        {
            Clients = clients.Distinct().ToList(),
            Years = years.Distinct().ToList(),
            Departments = departments.Distinct().ToList(),
            JobOrder = request.JobOrder ?? _defaults.JobOrder,
            MaxAttempts = request.MaxAttempts ?? _defaults.MaxAttempts,
            OutputRootDirectory = _defaults.OutputRootDirectory,
            DiagnosticsDirectory = _defaults.DiagnosticsDirectory,
            EnableContentValidation = request.EnableContentValidation ?? _defaults.EnableContentValidation,
            EnableChecksum = request.EnableChecksum ?? _defaults.EnableChecksum,
            Timeouts = _defaults.Timeouts,
            Simulation = request.Simulation ?? _defaults.Simulation
        };
    }
}
