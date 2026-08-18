using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Application.Validators;
using WilkenAutomation.Infrastructure.Configuration;
using WilkenAutomation.Worker.Realtime;
using WilkenAutomation.Worker.Wilken;
using WilkenAutomation.Worker.Windows;
using WilkenAutomation.Worker.Worker;

// ---------------------------------------------------------------------------
// Control-discovery POC (Phase 5): dotnet run -- --inspect "Wilken CS/2"
// Dumps the UIA control tree of the matching window so selectors can be mapped.
// ---------------------------------------------------------------------------
if (args.Length >= 1 && args[0] == "--inspect")
{
    var title = args.Length >= 2 ? args[1] : "Wilken";
    var dump = UiaTreeDumper.DumpWindowByTitle(title);
    var outputPath = Path.GetFullPath($"wilken-controls-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    File.WriteAllText(outputPath, dump);
    Console.WriteLine(dump);
    Console.WriteLine($"\nControl tree written to {outputPath}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

var wilkenOptions = builder.Configuration.GetSection(WilkenOptions.Section).Get<WilkenOptions>() ?? new WilkenOptions();
var exportSettings = builder.Configuration.GetSection(ExportSettings.Section).Get<ExportSettings>() ?? new ExportSettings();
var workerSettings = builder.Configuration.GetSection(WorkerSettings.Section).Get<WorkerSettings>() ?? new WorkerSettings();

builder.Services.AddSingleton(wilkenOptions);
builder.Services.AddSingleton(exportSettings);
builder.Services.AddSingleton(workerSettings);

builder.Services.AddWilkenInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IWilkenCredentialProvider, ConfigurationCredentialProvider>();
builder.Services.AddSingleton<IExportFileValidator, ExportFileValidator>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton(sp =>
{
    var state = new WorkerState { Mode = workerSettings.AutomationMode };
    return state;
});

// Automation mode switch: the executor and worker loop are identical for both.
if (workerSettings.AutomationMode == AutomationMode.Wilken)
{
    builder.Services.AddSingleton<IWilkenAutomationService, WindowsWilkenAutomationService>();
    builder.Services.AddSingleton<IScreenshotService, ScreenCaptureService>();
}
else
{
    builder.Services.AddSingleton<IWilkenAutomationService, MockWilkenAutomationService>();
    builder.Services.AddSingleton<IScreenshotService, TextEvidenceScreenshotService>();
}

builder.Services.AddScoped<StartupRecoveryService>();
builder.Services.AddScoped<JobExecutor>();

builder.Services.AddHostedService<JobWorker>();
builder.Services.AddHostedService<HeartbeatService>();

var host = builder.Build();

// The API owns schema creation; the worker just tolerates the DB not being ready yet.
host.Run();
