using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services.Exporting;

/// <summary>
/// Open evaluation → set required parameters → snapshot spool → start →
/// wait for completion → identify the new matching spool → export.
/// </summary>
public sealed class SpoolExecutor : IExportExecutor
{
    public string ExecutorType => ExecutorTypes.Spool;

    public async Task<string> RunAsync(
        ExportJob job,
        RunConfig config,
        ExportDefinition definition,
        IWilkenAutomationService wilken,
        Func<ApplicationState, Task> setState,
        CancellationToken cancellationToken)
    {
        await ApplyRequiredParametersAsync(job, definition, wilken, setState, cancellationToken);

        await setState(ApplicationState.OpeningSpool);
        await wilken.CaptureSpoolSnapshotAsync(cancellationToken);

        await setState(ApplicationState.StartingReport);
        await wilken.StartEvaluationAsync(cancellationToken);

        await setState(ApplicationState.WaitingForReport);
        await wilken.WaitForReportReadyAsync(cancellationToken);

        await setState(ApplicationState.OpeningSpool);
        await wilken.OpenSpoolAsync(cancellationToken);

        await setState(ApplicationState.Exporting);
        return await wilken.ExportAsync(job, cancellationToken);
    }

    internal static async Task ApplyRequiredParametersAsync(
        ExportJob job,
        ExportDefinition definition,
        IWilkenAutomationService wilken,
        Func<ApplicationState, Task> setState,
        CancellationToken ct)
    {
        if (definition.RequiresDimension(ExportDimensions.Client))
        {
            await setState(ApplicationState.SelectingClient);
            await wilken.SelectClientAsync(job.Client, ct);
        }

        await setState(ApplicationState.OpeningAssetAccounting);
        await wilken.OpenExportDefinitionAsync(definition.Name, ct);

        if (definition.RequiresDimension(ExportDimensions.Year) && job.FiscalYear > 0)
        {
            await setState(ApplicationState.SettingYear);
            await wilken.SetFiscalYearAsync(job.FiscalYear, ct);
        }

        if (definition.RequiresDimension(ExportDimensions.AccountingLaw))
        {
            await setState(ApplicationState.SelectingDepartment);
            await wilken.SelectDepartmentAsync(job.AccountingLaw ?? job.Department, ct);
        }
    }
}
