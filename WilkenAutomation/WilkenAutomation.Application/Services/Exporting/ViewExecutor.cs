using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services.Exporting;

/// <summary>
/// Open view → set required parameters → execute → wait for data ready → export.
/// No spool list is opened.
/// </summary>
public sealed class ViewExecutor : IExportExecutor
{
    public string ExecutorType => ExecutorTypes.View;

    public async Task<string> RunAsync(
        ExportJob job,
        RunConfig config,
        ExportDefinition definition,
        IWilkenAutomationService wilken,
        Func<ApplicationState, Task> setState,
        CancellationToken cancellationToken)
    {
        await SpoolExecutor.ApplyRequiredParametersAsync(job, definition, wilken, setState, cancellationToken);

        await setState(ApplicationState.StartingReport);
        await wilken.StartEvaluationAsync(cancellationToken);

        await setState(ApplicationState.WaitingForReport);
        await wilken.WaitForReportReadyAsync(cancellationToken);

        await setState(ApplicationState.Exporting);
        return await wilken.ExportAsync(job, cancellationToken);
    }
}
