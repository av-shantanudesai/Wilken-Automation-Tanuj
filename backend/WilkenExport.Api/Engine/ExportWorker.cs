using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WilkenExport.Api.Adapters;
using WilkenExport.Api.Configuration;
using WilkenExport.Api.Data;
using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Engine;

/// <summary>
/// The single worker (version 1). Picks the next eligible job of the active run,
/// drives the Wilken adapter through the full export workflow, validates the result,
/// computes the checksum and finalizes the job. All state transitions are persisted
/// immediately, so a crash at any point is recovered on restart without repeating
/// completed exports.
/// </summary>
public class ExportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWilkenAdapter _wilken;
    private readonly WorkerStatusService _status;
    private readonly FileValidator _validator;
    private readonly ChecksumService _checksum;
    private readonly ILogger<ExportWorker> _logger;

    public ExportWorker(
        IServiceScopeFactory scopeFactory,
        IWilkenAdapter wilken,
        WorkerStatusService status,
        FileValidator validator,
        ChecksumService checksum,
        ILogger<ExportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _wilken = wilken;
        _status = status;
        _validator = validator;
        _checksum = checksum;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RecoverStaleJobsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(ct);
                if (!processed)
                {
                    _status.SetIdle();
                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The worker loop itself must never die from a single failure.
                _logger.LogError(ex, "Unexpected error in worker loop");
                _status.ReportError(ex.Message);
                await Task.Delay(2000, ct);
            }
        }
    }

    /// <summary>
    /// Restart capability: jobs left in RUNNING by a crash/power failure are re-queued
    /// as RETRY without consuming an extra attempt beyond the interrupted one.
    /// </summary>
    private async Task RecoverStaleJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();

        var stale = await db.Jobs.Where(j => j.Status == JobStatus.Running).ToListAsync(ct);
        foreach (var job in stale)
        {
            job.Status = job.AttemptCount >= MaxAttemptsFor(db, job) ? JobStatus.FailedFinal : JobStatus.Retry;
            job.ErrorCode = "INTERRUPTED";
            job.ErrorMessage = "Job was interrupted by an automation/system restart and has been re-queued.";
            job.UpdatedAt = DateTime.UtcNow;
            db.Logs.Add(Log(job, "WARN", "RECOVERED_STALE", job.ErrorMessage, job.AttemptCount));
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("Recovered {Count} stale RUNNING job(s) after restart", stale.Count);
        }
    }

    private static int MaxAttemptsFor(ExportDbContext db, ExportJob job)
    {
        var run = db.Runs.Find(job.RunId);
        var config = run == null ? new RunConfig() : JsonSerializer.Deserialize<RunConfig>(run.ConfigJson) ?? new RunConfig();
        return config.MaxAttempts;
    }

    private async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();

        var run = await db.Runs
            .Where(r => r.Status == RunStatus.Running)
            .OrderBy(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (run == null) return false;

        var config = JsonSerializer.Deserialize<RunConfig>(run.ConfigJson) ?? new RunConfig();

        var job = await db.Jobs
            .Where(j => j.RunId == run.Id && (j.Status == JobStatus.Pending || j.Status == JobStatus.Retry))
            .OrderBy(j => j.OrderIndex)
            .FirstOrDefaultAsync(ct);

        if (job == null)
        {
            await CompleteRunAsync(db, run, ct);
            return true;
        }

        await ProcessJobAsync(db, run, config, job, ct);
        return true;
    }

    private async Task ProcessJobAsync(ExportDbContext db, ExportRun run, RunConfig config, ExportJob job, CancellationToken ct)
    {
        job.AttemptCount++;
        job.Status = JobStatus.Running;
        job.StartTime = DateTime.UtcNow;
        job.EndTime = null;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.UpdatedAt = DateTime.UtcNow;
        db.Logs.Add(Log(job, "INFO", "START", $"Attempt {job.AttemptCount}/{config.MaxAttempts} started.", job.AttemptCount));
        await db.SaveChangesAsync(ct);

        _status.SetJob(run.Id, job.Id, $"Client {job.Client} / {job.FiscalYear} / {job.Department}", job.AttemptCount);
        var stopwatch = Stopwatch.StartNew();

        var targetDir = Path.Combine(config.OutputRootDirectory, run.Id, $"Mandant_{job.Client}", job.FiscalYear.ToString());
        var baseFileName = $"Mandant_{job.Client}_{job.FiscalYear}_{job.Department}.csv";
        var tempPath = Path.Combine(targetDir, $"{baseFileName}.attempt{job.AttemptCount}.tmp");

        try
        {
            await Step(db, job, "PREPARE_SESSION", () => _wilken.EnsureSessionAsync(config, ct), ct);
            await Step(db, job, "SELECT_CLIENT", () => _wilken.SelectClientAsync(job.Client, ct), ct);
            await Step(db, job, "OPEN_ASSET_ACCOUNTING", () => _wilken.OpenAssetAccountingAsync(ct), ct);
            await Step(db, job, "SET_FISCAL_YEAR", () => _wilken.SetFiscalYearAsync(job.FiscalYear, ct), ct);
            await Step(db, job, "SELECT_DEPARTMENT", () => _wilken.SelectDepartmentAsync(job.Department, ct), ct);

            _status.SetAction("EXECUTE_REPORT");
            var reportId = await _wilken.StartEvaluationAsync(job, ct);
            db.Logs.Add(Log(job, "INFO", "EXECUTE_REPORT", $"Evaluation started, report id {reportId}.", job.AttemptCount));
            await db.SaveChangesAsync(ct);

            _status.SetAction("WAIT_SPOOL");
            var spool = await _wilken.WaitForSpoolAsync(
                reportId, job, TimeSpan.FromSeconds(config.Timeouts.SpoolSeconds), ct);
            db.Logs.Add(Log(job, "INFO", "SPOOL_READY", $"Spool {spool.ReportId} ready, reported record count {spool.RecordCount}.", job.AttemptCount));
            await db.SaveChangesAsync(ct);

            _status.SetAction("EXPORT");
            await _wilken.ExportSpoolAsync(spool, job, tempPath, ct);
            db.Logs.Add(Log(job, "INFO", "EXPORT", $"Original export written to temporary file.", job.AttemptCount));

            _status.SetAction("VALIDATE");
            var validation = await _validator.ValidateAsync(tempPath, job, config.EnableContentValidation, ct);
            job.ValidationOutcome = validation.Outcome;
            job.ValidationDetail = validation.Detail;
            job.RecordCount = validation.RecordCount;
            db.Logs.Add(Log(job, validation.Outcome == ValidationOutcome.Invalid ? "ERROR" : "INFO",
                "VALIDATE", validation.Detail, job.AttemptCount));

            if (validation.Outcome == ValidationOutcome.Invalid)
                throw new WilkenException("VALIDATION_FAILED", validation.Detail);

            if (config.EnableChecksum)
            {
                _status.SetAction("CHECKSUM");
                job.Sha256 = await _checksum.ComputeSha256Async(tempPath, ct);
                db.Logs.Add(Log(job, "INFO", "CHECKSUM", $"SHA-256 {job.Sha256}", job.AttemptCount));
            }

            _status.SetAction("SAVE_FILE");
            var finalPath = ResolveFinalPath(targetDir, baseFileName, job.AttemptCount);
            File.Move(tempPath, finalPath);
            var info = new FileInfo(finalPath);

            job.FileName = info.Name;
            job.FilePath = info.FullName;
            job.FileSizeBytes = info.Length;
            job.EndTime = DateTime.UtcNow;
            job.DurationMs = stopwatch.ElapsedMilliseconds;
            job.Status = validation.Outcome == ValidationOutcome.ValidEmpty
                ? JobStatus.SuccessEmpty
                : JobStatus.SuccessWithData;
            job.UpdatedAt = DateTime.UtcNow;

            db.Logs.Add(Log(job, "INFO", "SUCCESS",
                $"{job.Status} in {job.DurationMs} ms, file {job.FileName} ({job.FileSizeBytes} bytes).", job.AttemptCount, job.DurationMs));
            await db.SaveChangesAsync(ct);
            _status.ReportSuccess(job.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: leave the job RUNNING; startup recovery re-queues it.
            throw;
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(db, config, job, ex, tempPath, stopwatch.ElapsedMilliseconds, ct);
        }
    }

    private async Task HandleFailureAsync(ExportDbContext db, RunConfig config, ExportJob job,
        Exception ex, string tempPath, long elapsedMs, CancellationToken ct)
    {
        var wilkenEx = ex as WilkenException;
        job.ErrorCode = wilkenEx?.ErrorCode ?? "UNEXPECTED_ERROR";
        job.ErrorMessage = ex.Message;
        job.EndTime = DateTime.UtcNow;
        job.DurationMs = elapsedMs;
        job.UpdatedAt = DateTime.UtcNow;
        _status.ReportError($"{job.Id}: {ex.Message}");

        // Diagnostic evidence (stand-in for a GUI screenshot in the simulated adapter).
        try
        {
            Directory.CreateDirectory(config.DiagnosticsDirectory);
            var diagPath = Path.Combine(config.DiagnosticsDirectory, $"{job.Id}_attempt{job.AttemptCount}.txt");
            await File.WriteAllTextAsync(diagPath,
                $"Job: {job.Id}\nAttempt: {job.AttemptCount}\nTime: {DateTime.UtcNow:O}\n" +
                $"ErrorCode: {job.ErrorCode}\nMessage: {ex}\nSession: {_wilken.SessionStatus}\n", ct);
            job.ScreenshotPath = diagPath;
        }
        catch (Exception diagEx)
        {
            _logger.LogWarning(diagEx, "Could not write diagnostics for {JobId}", job.Id);
        }

        // A failed attempt must never leave a partial file behind as if it were an export.
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }

        var retryAllowed = job.AttemptCount < config.MaxAttempts;
        job.Status = retryAllowed ? JobStatus.Retry : JobStatus.FailedFinal;

        db.Logs.Add(Log(job, "ERROR", retryAllowed ? "FAILED_ATTEMPT" : "FAILED_FINAL",
            $"[{job.ErrorCode}] {ex.Message}", job.AttemptCount, elapsedMs, job.ErrorCode));
        await db.SaveChangesAsync(ct);

        // Deterministic recovery: never keep clicking into an unknown application state.
        if (wilkenEx?.SessionLost == true)
        {
            _status.SetAction("RECOVER_SESSION");
            db.Logs.Add(Log(job, "WARN", "RECOVER_SESSION", "Session lost - restarting Wilken session.", job.AttemptCount));
            await db.SaveChangesAsync(ct);
            await _wilken.RecoverSessionAsync(ct);
        }
    }

    private async Task CompleteRunAsync(ExportDbContext db, ExportRun run, CancellationToken ct)
    {
        var stillRunning = await db.Jobs.AnyAsync(j => j.RunId == run.Id && j.Status == JobStatus.Running, ct);
        if (stillRunning) return;

        run.Status = RunStatus.Completed;
        run.CompletedAt = DateTime.UtcNow;

        // Completeness check from persistent job records, not from output-folder file counts.
        var counts = await db.Jobs.Where(j => j.RunId == run.Id)
            .GroupBy(j => j.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(c => c.Count);
        var summary = string.Join(", ", counts.Select(c => $"{c.Key}={c.Count}"));
        var reconciles = total == run.ExpectedJobCount;

        db.Logs.Add(new JobLogEntry
        {
            Timestamp = DateTime.UtcNow,
            RunId = run.Id,
            Level = reconciles ? "INFO" : "ERROR",
            Action = "RUN_COMPLETED",
            Message = $"Run finished. Expected={run.ExpectedJobCount}, Accounted={total} ({summary}). " +
                      (reconciles ? "Completeness check passed." : "COMPLETENESS MISMATCH - investigate!")
        });
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Run {RunId} completed: {Summary}", run.Id, summary);
    }

    /// <summary>Never silently overwrite: an existing file forces a distinct retry filename.</summary>
    private static string ResolveFinalPath(string dir, string baseFileName, int attempt)
    {
        Directory.CreateDirectory(dir);
        var candidate = Path.Combine(dir, baseFileName);
        if (!File.Exists(candidate)) return candidate;

        var name = Path.GetFileNameWithoutExtension(baseFileName);
        var ext = Path.GetExtension(baseFileName);
        candidate = Path.Combine(dir, $"{name}_r{attempt}{ext}");
        var i = 1;
        while (File.Exists(candidate))
            candidate = Path.Combine(dir, $"{name}_r{attempt}_{i++}{ext}");
        return candidate;
    }

    private async Task Step(ExportDbContext db, ExportJob job, string action, Func<Task> work, CancellationToken ct)
    {
        _status.SetAction(action);
        var sw = Stopwatch.StartNew();
        await work();
        db.Logs.Add(Log(job, "INFO", action, "OK", job.AttemptCount, sw.ElapsedMilliseconds));
        await db.SaveChangesAsync(ct);
    }

    private static JobLogEntry Log(ExportJob job, string level, string action, string message,
        int? attempt = null, long? durationMs = null, string? errorCode = null) => new()
    {
        Timestamp = DateTime.UtcNow,
        RunId = job.RunId,
        JobId = job.Id,
        Client = job.Client,
        FiscalYear = job.FiscalYear,
        Department = job.Department,
        Level = level,
        Action = action,
        Attempt = attempt,
        DurationMs = durationMs,
        ErrorCode = errorCode,
        Message = message
    };
}
