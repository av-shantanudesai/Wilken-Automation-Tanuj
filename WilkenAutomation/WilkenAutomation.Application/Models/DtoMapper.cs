namespace WilkenAutomation.Application.Models;

/// <summary>Entity-to-DTO mapping. Kept explicit (no reflection mapper) so wire shape changes are visible in review.</summary>
public static class DtoMapper
{
    public static JobDto ToDto(this ExportJob job) => new()
    {
        Id = job.JobId,
        RunId = job.RunId,
        Client = job.Client,
        FiscalYear = job.FiscalYear,
        Department = job.Department,
        DepartmentCode = job.DepartmentCode,
        ExportDefinition = job.ExportDefinition,
        ExecutorType = job.ExecutorType,
        Period = job.Period,
        AccountingLaw = job.AccountingLaw,
        SpoolId = job.SpoolId,
        OrderIndex = job.OrderIndex,
        Status = job.Status,
        AttemptCount = job.AttemptCount,
        StartTime = job.StartTime,
        EndTime = job.EndTime,
        DurationMs = job.DurationMs,
        FileName = job.FileName,
        FilePath = job.FilePath,
        FileSizeBytes = job.FileSize,
        Sha256 = job.Sha256,
        ValidationOutcome = job.ValidationStatus,
        ValidationDetail = job.ValidationDetail,
        RecordCount = job.RecordCount,
        ErrorCode = job.ErrorCode,
        ErrorMessage = job.ErrorMessage,
        ScreenshotPath = job.LastScreenshotPath,
        ApplicationState = job.ApplicationState,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt
    };

    public static LogEntryDto ToDto(this AutomationLog log) => new()
    {
        Id = log.Id,
        Timestamp = log.Timestamp,
        RunId = log.RunId,
        JobId = log.JobId,
        Client = log.Client,
        FiscalYear = log.FiscalYear,
        Department = log.Department,
        Level = log.Level,
        Action = log.Action,
        Attempt = log.Attempt,
        DurationMs = log.DurationMs,
        ErrorCode = log.ErrorCode,
        Message = log.Message
    };

    public static RunSummaryDto ToSummaryDto(this AutomationRun run, StatusCountsDto counts) => new()
    {
        Id = run.RunId,
        Status = run.Status,
        CreatedAt = run.CreatedAt,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        ExpectedJobCount = run.ExpectedJobs,
        GeneratedJobCount = run.TotalJobs,
        JobCountDeviation = run.ExpectedJobs != run.TotalJobs,
        Notes = run.Notes,
        Counts = counts
    };
}
