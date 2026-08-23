using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Interfaces;

/// <summary>
/// Structured, classified automation failure. SessionLost=true means the Wilken
/// session must be recovered before another attempt is made.
/// </summary>
public class WilkenAutomationException : Exception
{
    public string ErrorCode { get; }
    public bool SessionLost { get; }

    public WilkenAutomationException(string errorCode, string message, bool sessionLost = false, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        SessionLost = sessionLost;
    }
}

/// <summary>
/// All Wilken CS/2 interaction lives behind this interface. Implementations:
///  - MockWilkenAutomationService     (simulation, no Wilken required)
///  - WindowsWilkenAutomationService  (real desktop automation in the worker agent)
/// The job executor is completely unaware of which one is active.
/// </summary>
public interface IWilkenAutomationService
{
    WilkenSessionStatus SessionStatus { get; }

    /// <summary>Called once per attempt before the workflow starts; carries job + run config context.</summary>
    Task BeginJobAsync(ExportJob job, RunConfig config, CancellationToken cancellationToken);

    Task EnsureSessionAsync(CancellationToken cancellationToken);
    Task SelectClientAsync(string client, CancellationToken cancellationToken);
    Task OpenAssetAccountingAsync(CancellationToken cancellationToken);
    Task SetFiscalYearAsync(int fiscalYear, CancellationToken cancellationToken);
    Task SelectDepartmentAsync(string department, CancellationToken cancellationToken);
    Task StartEvaluationAsync(CancellationToken cancellationToken);
    Task WaitForReportReadyAsync(CancellationToken cancellationToken);
    Task OpenSpoolAsync(CancellationToken cancellationToken);

    /// <summary>Snapshot spool identities before starting a SPOOL evaluation.</summary>
    Task CaptureSpoolSnapshotAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Open the screen for the named export definition. Defaults to the asset-accounting entry.</summary>
    Task OpenExportDefinitionAsync(string definitionName, CancellationToken cancellationToken) =>
        OpenAssetAccountingAsync(cancellationToken);

    /// <summary>Exports the report and returns the path of the produced (temporary) file.</summary>
    Task<string> ExportAsync(ExportJob job, CancellationToken cancellationToken);

    Task<bool> IsSessionHealthyAsync(CancellationToken cancellationToken);
    Task RecoverSessionAsync(CancellationToken cancellationToken);
}
