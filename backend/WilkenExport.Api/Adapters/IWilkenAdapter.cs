using WilkenExport.Api.Configuration;
using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Adapters;

/// <summary>
/// Structured, classified failure raised by the Wilken adapter.
/// SessionLost=true means the session must be recovered before the next attempt.
/// </summary>
public class WilkenException : Exception
{
    public string ErrorCode { get; }
    public bool SessionLost { get; }

    public WilkenException(string errorCode, string message, bool sessionLost = false)
        : base(message)
    {
        ErrorCode = errorCode;
        SessionLost = sessionLost;
    }
}

public record SpoolInfo(string ReportId, int RecordCount);

/// <summary>
/// Isolates all Wilken CS/2 specific interaction from job management, validation,
/// persistence and monitoring. The production implementation will use UI Automation /
/// a programmatic interface (whichever the integration assessment selects); the
/// simulated implementation generates dummy exports for end-to-end testing.
/// </summary>
public interface IWilkenAdapter
{
    /// <summary>Current session health, exposed for monitoring (e.g. NotStarted, Ready, Crashed).</summary>
    string SessionStatus { get; }

    /// <summary>Start Wilken if not running, reuse a healthy session, log in if required.</summary>
    Task EnsureSessionAsync(RunConfig config, CancellationToken ct);

    Task SelectClientAsync(string client, CancellationToken ct);

    Task OpenAssetAccountingAsync(CancellationToken ct);

    Task SetFiscalYearAsync(int year, CancellationToken ct);

    Task SelectDepartmentAsync(string department, CancellationToken ct);

    /// <summary>Starts the evaluation and returns a report/process identifier.</summary>
    Task<string> StartEvaluationAsync(ExportJob job, CancellationToken ct);

    /// <summary>
    /// State-based readiness detection: polls the (simulated) spool until the report
    /// for this job is available, or the configured timeout elapses.
    /// </summary>
    Task<SpoolInfo> WaitForSpoolAsync(string reportId, ExportJob job, TimeSpan timeout, CancellationToken ct);

    /// <summary>Triggers the export and writes the original file to tempFilePath.</summary>
    Task ExportSpoolAsync(SpoolInfo spool, ExportJob job, string tempFilePath, CancellationToken ct);

    /// <summary>Close (gracefully if possible) and restart the session after a failure.</summary>
    Task RecoverSessionAsync(CancellationToken ct);
}
