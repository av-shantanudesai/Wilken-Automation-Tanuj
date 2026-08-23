using Microsoft.Extensions.Logging;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

/// <summary>Outcome of a single job attempt, reported back to the worker loop.</summary>
public record JobExecutionResult(JobStatus FinalStatus, bool SessionLost);

/// <summary>
/// Executes one attempt of one export job through the complete 16-step workflow.
/// Persistence-first: every state change is written to the database before the
/// corresponding SignalR event is published. Automation-technology agnostic -
/// works identically against the mock and the real Windows implementation.
/// </summary>
public class JobExecutor
{
    private readonly IJobRepository _jobs;
    private readonly IRunRepository _runs;
    private readonly ILogRepository _logs;
    private readonly IWilkenAutomationService _wilken;
    private readonly IReadOnlyDictionary<string, IExportExecutor> _executors;
    private readonly ExportDefinitionCatalog _catalog;
    private readonly IExportFileValidator _validator;
    private readonly IChecksumService _checksum;
    private readonly IScreenshotService _screenshots;
    private readonly IRealtimeNotifier _notifier;
    private readonly ExportSettings _exportSettings;
    private readonly WilkenOptions _wilkenOptions;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(
        IJobRepository jobs,
        IRunRepository runs,
        ILogRepository logs,
        IWilkenAutomationService wilken,
        IEnumerable<IExportExecutor> executors,
        ExportDefinitionCatalog catalog,
        IExportFileValidator validator,
        IChecksumService checksum,
        IScreenshotService screenshots,
        IRealtimeNotifier notifier,
        ExportSettings exportSettings,
        WilkenOptions wilkenOptions,
        ILogger<JobExecutor> logger)
    {
        _jobs = jobs;
        _runs = runs;
        _logs = logs;
        _wilken = wilken;
        _executors = executors.ToDictionary(e => e.ExecutorType, e => e, StringComparer.OrdinalIgnoreCase);
        _catalog = catalog;
        _validator = validator;
        _checksum = checksum;
        _screenshots = screenshots;
        _notifier = notifier;
        _exportSettings = exportSettings;
        _wilkenOptions = wilkenOptions;
        _logger = logger;
    }

    public event Func<ExportJob, ApplicationState, Task>? ApplicationStateChanged;

    public async Task<JobExecutionResult> ExecuteAsync(ExportJob job, RunConfig config, CancellationToken ct)
    {
        // Step 1 - persist RUNNING + start time, then notify.
        JobStateMachine.EnsureTransition(job.Status, JobStatus.Running);
        var attemptNumber = job.AttemptCount + 1;
        var attempt = new JobAttempt
        {
            JobId = job.JobId,
            AttemptNumber = attemptNumber,
            StartTime = DateTime.UtcNow,
            Status = "Running",
            CreatedAt = DateTime.UtcNow
        };

        job.Status = JobStatus.Running;
        job.AttemptCount = attemptNumber;
        job.StartTime = DateTime.UtcNow;
        job.EndTime = null;
        job.DurationMs = null;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _jobs.UpdateAsync(job, ct);
        await _jobs.AddAttemptAsync(attempt, ct);

        var owner = await _runs.GetByRunIdAsync(job.RunId, ct);
        var userId = owner?.UserId;

        await LogAsync(job, "INFO", "JobStarted", $"Attempt {attemptNumber} started.", attemptNumber, ct: ct);
        await Notify(SignalREvents.JobStarted, job.ToDto(), userId, job.RunId, ct);
        await Notify(SignalREvents.JobStatusChanged, job.ToDto(), userId, job.RunId, ct);

        try
        {
            await _wilken.BeginJobAsync(job, config, ct);

            // Step 2 - session
            await SetState(job, ApplicationState.StartingWilken, userId, ct);
            if (!await _wilken.IsSessionHealthyAsync(ct))
            {
                await SetState(job, ApplicationState.RecoveringSession, userId, ct);
                await _wilken.RecoverSessionAsync(ct);
            }
            await _wilken.EnsureSessionAsync(ct);

            var definition = _catalog.ResolveForJob(job);
            job.ExportDefinition = definition.Name;
            job.ExecutorType = definition.Type;
            if (string.IsNullOrWhiteSpace(job.AccountingLaw))
                job.AccountingLaw = job.Department;

            if (!_executors.TryGetValue(definition.Type, out var executor))
                throw new WilkenAutomationException("EXECUTOR_NOT_FOUND", $"No executor registered for '{definition.Type}'.");

            var producedPath = await executor.RunAsync(
                job,
                config,
                definition,
                _wilken,
                state => SetState(job, state, userId, ct),
                ct);
            await LogAsync(job, "INFO", "ReportReady", $"{definition.Name} ({definition.Type}) completed.", attemptNumber, ct: ct);

            // Step 11 - wait until file creation actually completed
            await SetState(job, ApplicationState.WaitingForFile, userId, ct);
            await WaitHelper.WaitForFileReadyAsync(
                producedPath,
                TimeSpan.FromSeconds(_wilkenOptions.FileCreationTimeoutSeconds),
                TimeSpan.FromMilliseconds(_wilkenOptions.PollingIntervalMs),
                ct);

            // Move the original into its final location without ever silently overwriting.
            var finalPath = PlaceOriginalFile(producedPath, job, attemptNumber, definition);
            var fileInfo = new FileInfo(finalPath);
            job.FileName = fileInfo.Name;
            job.FilePath = fileInfo.FullName;
            job.FileSize = fileInfo.Length;

            // Step 12 - validate
            await SetState(job, ApplicationState.ValidatingFile, userId, ct);
            var validation = await _validator.ValidateAsync(finalPath, job, config.EnableContentValidation, ct);
            job.ValidationStatus = validation.Status;
            job.ValidationDetail = validation.Detail;
            job.RecordCount = validation.RecordCount;

            if (validation.Status == ValidationStatus.Invalid)
                throw new WilkenAutomationException("VALIDATION_FAILED", $"Export validation failed: {validation.Detail}");

            // Step 13 - checksum
            if (config.EnableChecksum)
            {
                await SetState(job, ApplicationState.CalculatingHash, userId, ct);
                job.Sha256 = await _checksum.ComputeSha256Async(finalPath, ct);
            }

            // Step 14 - persist final result (transactional with attempt + run counters)
            var finalStatus = validation.Status == ValidationStatus.ValidEmpty
                ? JobStatus.SuccessEmpty
                : JobStatus.SuccessWithData;
            JobStateMachine.EnsureTransition(job.Status, finalStatus);

            job.Status = finalStatus;
            job.EndTime = DateTime.UtcNow;
            job.DurationMs = (long)(job.EndTime.Value - job.StartTime!.Value).TotalMilliseconds;
            job.ApplicationState = ApplicationState.Completed.ToWireName();
            job.UpdatedAt = DateTime.UtcNow;

            attempt.EndTime = job.EndTime;
            attempt.Status = "Succeeded";
            await _jobs.SaveJobTransitionAsync(job, attempt, job.RunId, ct);

            await LogAsync(job, "INFO", "JobCompleted",
                $"{finalStatus} - {job.FileName} ({job.FileSize} bytes, {job.RecordCount ?? 0} records).",
                attemptNumber, job.DurationMs, ct: ct);

            // Step 15 - notify after persistence
            await Notify(SignalREvents.JobCompleted, job.ToDto(), userId, job.RunId, ct);
            await Notify(SignalREvents.JobStatusChanged, job.ToDto(), userId, job.RunId, ct);
            await Notify(SignalREvents.LastSuccessChanged, new { jobId = job.JobId, at = job.EndTime }, userId, job.RunId, ct);
            await PublishRunProgressAsync(job.RunId, userId, ct);

            return new JobExecutionResult(finalStatus, SessionLost: false);
        }
        catch (OperationCanceledException)
        {
            // Timeout or host shutdown: persist RETRY with a non-cancelled token so
            // the row is never left RUNNING for the rest of a week-long process.
            return await HandleFailureAsync(
                job,
                attempt,
                config,
                new WaitTimeoutException("Job attempt cancelled or exceeded the hung-job timeout."),
                userId,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            return await HandleFailureAsync(job, attempt, config, ex, userId, ct);
        }
    }

    private async Task<JobExecutionResult> HandleFailureAsync(
        ExportJob job, JobAttempt attempt, RunConfig config, Exception ex, long? userId, CancellationToken ct)
    {
        var errorCode = (ex as WilkenAutomationException)?.ErrorCode
            ?? (ex is WaitTimeoutException ? "TIMEOUT" : "UNEXPECTED_ERROR");
        var sessionLost = (ex as WilkenAutomationException)?.SessionLost ?? false;
        if (!sessionLost)
        {
            try { sessionLost = !await _wilken.IsSessionHealthyAsync(ct); }
            catch { sessionLost = true; }
        }

        _logger.LogError(ex, "Job {JobId} attempt {Attempt} failed with {ErrorCode}",
            job.JobId, attempt.AttemptNumber, errorCode);

        var screenshotPath = await _screenshots.CaptureAsync(
            job.RunId, job.JobId, attempt.AttemptNumber, errorCode, ct);

        var exhausted = attempt.AttemptNumber >= config.MaxAttempts;
        var nextStatus = exhausted ? JobStatus.FailedFinal : JobStatus.Retry;
        JobStateMachine.EnsureTransition(JobStatus.Running, nextStatus);

        job.Status = nextStatus;
        job.EndTime = DateTime.UtcNow;
        job.DurationMs = job.StartTime is null ? null : (long)(job.EndTime.Value - job.StartTime.Value).TotalMilliseconds;
        job.ErrorCode = errorCode;
        job.ErrorMessage = Truncate(ex.Message, 1000);
        job.LastScreenshotPath = screenshotPath ?? job.LastScreenshotPath;
        job.ApplicationState = ApplicationState.Idle.ToWireName();
        job.UpdatedAt = DateTime.UtcNow;

        attempt.EndTime = job.EndTime;
        attempt.Status = "Failed";
        attempt.ErrorCode = errorCode;
        attempt.ErrorMessage = job.ErrorMessage;
        attempt.ScreenshotPath = screenshotPath;

        await _jobs.SaveJobTransitionAsync(job, attempt, job.RunId, ct);
        await LogAsync(job, "ERROR", "JobFailed",
            $"Attempt {attempt.AttemptNumber} failed ({errorCode}): {ex.Message} -> {nextStatus}",
            attempt.AttemptNumber, job.DurationMs, errorCode, ct);

        await Notify(SignalREvents.JobFailed, job.ToDto(), userId, job.RunId, ct);
        await Notify(
            nextStatus == JobStatus.Retry ? SignalREvents.JobRetrying : SignalREvents.JobStatusChanged,
            job.ToDto(), userId, job.RunId, ct);
        await Notify(SignalREvents.LastErrorChanged,
            new { jobId = job.JobId, errorCode, message = job.ErrorMessage, at = job.EndTime }, userId, job.RunId, ct);
        await PublishRunProgressAsync(job.RunId, userId, ct);

        return new JobExecutionResult(nextStatus, sessionLost);
    }

    /// <summary>
    /// Moves the Wilken-produced original into /Exports/Mandant_xxx/yyyy/ without
    /// modification. An existing file is never silently overwritten - retries get
    /// a unique attempt-suffixed name instead.
    /// </summary>
    private string PlaceOriginalFile(string producedPath, ExportJob job, int attemptNumber, ExportDefinition definition)
    {
        var extension = Path.GetExtension(producedPath);
        if (string.IsNullOrEmpty(extension))
        {
            var format = definition.Export.Format;
            extension = string.IsNullOrWhiteSpace(format) ? _exportSettings.FileExtension : "." + format.Trim().TrimStart('.');
        }

        var directory = ExportFilename.DirectoryFor(job, _exportSettings.RootDirectory, definition.Export.Directory);
        Directory.CreateDirectory(directory);

        var baseName = Path.GetFileNameWithoutExtension(ExportFilename.Render(definition.Export.Filename, job));
        var target = Path.Combine(directory, baseName + extension);
        if (File.Exists(target))
            target = Path.Combine(directory, $"{baseName}_attempt{attemptNumber}_{DateTime.UtcNow:HHmmss}{extension}");

        File.Move(producedPath, target);
        return target;
    }

    private async Task SetState(ExportJob job, ApplicationState state, long? userId, CancellationToken ct)
    {
        job.ApplicationState = state.ToWireName();
        job.UpdatedAt = DateTime.UtcNow;
        await _jobs.UpdateAsync(job, ct);

        if (ApplicationStateChanged is not null)
            await ApplicationStateChanged.Invoke(job, state);

        await Notify(SignalREvents.JobApplicationStateChanged, new
        {
            runId = job.RunId,
            jobId = job.JobId,
            client = job.Client,
            fiscalYear = job.FiscalYear,
            department = job.Department,
            status = job.Status.ToString(),
            attemptCount = job.AttemptCount,
            applicationState = job.ApplicationState,
            runtimeSeconds = job.StartTime is null ? 0 : (DateTime.UtcNow - job.StartTime.Value).TotalSeconds
        }, userId, job.RunId, ct);
    }

    private async Task PublishRunProgressAsync(string runId, long? userId, CancellationToken ct)
    {
        var counts = await _runs.GetCountsAsync(runId, ct);
        await Notify(SignalREvents.RunProgressChanged, new
        {
            runId,
            counts,
            progressPercent = RunStatisticsService.ProgressPercent(counts)
        }, userId, runId, ct);
        await Notify(SignalREvents.DashboardSummaryChanged, new { runId }, userId, runId, ct);
    }

    private Task Notify(string eventName, object payload, long? userId, string? runId, CancellationToken ct) =>
        _notifier.PublishAsync(eventName, payload, ct, userId, runId);

    private Task LogAsync(ExportJob job, string level, string action, string message,
        int? attempt = null, long? durationMs = null, string? errorCode = null, CancellationToken ct = default)
        => _logs.AddAsync(new AutomationLog
        {
            RunId = job.RunId,
            JobId = job.JobId,
            Client = job.Client,
            FiscalYear = job.FiscalYear,
            Department = job.Department,
            Timestamp = DateTime.UtcNow,
            Level = level,
            Action = action,
            Message = message,
            ApplicationState = job.ApplicationState,
            Attempt = attempt,
            DurationMs = durationMs,
            ErrorCode = errorCode
        }, ct);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
