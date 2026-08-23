using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Interfaces;

public interface IExportExecutor
{
    string ExecutorType { get; }

    Task<string> RunAsync(
        ExportJob job,
        RunConfig config,
        ExportDefinition definition,
        IWilkenAutomationService wilken,
        Func<ApplicationState, Task> setState,
        CancellationToken cancellationToken);
}
