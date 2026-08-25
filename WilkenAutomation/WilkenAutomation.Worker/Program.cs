using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Application.Services.Exporting;
using WilkenAutomation.Application.Validators;
using WilkenAutomation.Infrastructure.Configuration;
using WilkenAutomation.Infrastructure.Database;
using WilkenAutomation.Worker.Realtime;
using WilkenAutomation.Worker.Wilken;
using WilkenAutomation.Worker.Windows;
using WilkenAutomation.Worker.Worker;

if (args.Length >= 1 && args[0] == "--inspect")
{
    var rest = args.Skip(1).ToArray();
    var dump = rest.Length == 0 || rest[0] is "--list" or "-l"
        ? UiaTreeDumper.ListTopLevelWindows()
        : UiaTreeDumper.DumpWindowByTitle(rest[0]);
    var outputPath = Path.GetFullPath($"wilken-controls-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    File.WriteAllText(outputPath, dump);
    Console.WriteLine(dump);
    Console.WriteLine($"\nControl tree written to {outputPath}");
    return;
}

if (args.Length >= 1 && args[0].Equals("--replica-smoke", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = await ReplicaSmokeRunner.RunAsync();
    return;
}

var forceDesktopTest = args.Any(a => a.Equals("--desktop-test", StringComparison.OrdinalIgnoreCase));
var hostArgs = args.Where(a => !a.Equals("--desktop-test", StringComparison.OrdinalIgnoreCase)).ToArray();

var builder = Host.CreateApplicationBuilder(hostArgs);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
if (forceDesktopTest)
{
    builder.Configuration.AddJsonFile("appsettings.DesktopTest.json", optional: true, reloadOnChange: false);
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Worker:AutomationMode"] = "DesktopTest",
        ["Wilken:Username"] = "",
        ["Wilken:Password"] = "",
        ["Wilken:MainWindowTitle"] = "Wilken_CS/2_Finanzmanagement",
        ["Wilken:ProcessName"] = "WilkenCs2ReplicaMock",
        ["Export:FileExtension"] = ".xlsx"
    });
}

var wilkenOptions = builder.Configuration.GetSection(WilkenOptions.Section).Get<WilkenOptions>() ?? new WilkenOptions();
var exportSettings = builder.Configuration.GetSection(ExportSettings.Section).Get<ExportSettings>() ?? new ExportSettings();
var workerSettings = builder.Configuration.GetSection(WorkerSettings.Section).Get<WorkerSettings>() ?? new WorkerSettings();

if (forceDesktopTest)
    workerSettings.AutomationMode = AutomationMode.DesktopTest;

if (workerSettings.AutomationMode == AutomationMode.DesktopTest)
{
    ApplyDesktopTestDefaults(wilkenOptions);
    ResolveDesktopTestExecutable(wilkenOptions, builder.Environment.ContentRootPath);
}

builder.Services.AddSingleton(wilkenOptions);
builder.Services.AddSingleton(exportSettings);
builder.Services.AddSingleton(workerSettings);
builder.Services.AddSingleton(builder.Configuration.GetSection(MaintenanceSettings.Section).Get<MaintenanceSettings>() ?? new MaintenanceSettings());

var jwtOptions = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
JwtTokenService.EnsureProductionKey(jwtOptions, builder.Environment.EnvironmentName);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(new JwtTokenService(jwtOptions));

builder.Services.AddWilkenInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IWilkenCredentialProvider, ConfigurationCredentialProvider>();
builder.Services.AddSingleton(ExportDefinitionCatalog.Load(builder.Environment.ContentRootPath));
builder.Services.AddSingleton<IExportExecutor, SpoolExecutor>();
builder.Services.AddSingleton<IExportExecutor, ViewExecutor>();
builder.Services.AddSingleton<IExportFileValidator, ExportFileValidator>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton(sp =>
{
    var state = new WorkerState { Mode = workerSettings.AutomationMode };
    return state;
});

if (workerSettings.AutomationMode is AutomationMode.Wilken or AutomationMode.DesktopTest)
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
builder.Services.AddHostedService<MaintenanceHostedService>();

Console.WriteLine();
Console.WriteLine("============================================================");
switch (workerSettings.AutomationMode)
{
    case AutomationMode.DesktopTest:
        Console.WriteLine("MODE: DesktopTest — Wilken CS/2 replica UI will open.");
        Console.WriteLine($"Exe: {wilkenOptions.ExecutablePath}");
        Console.WriteLine("Handelsrecht → Zugangsliste; Steuerrecht → Anlagenspiegel. Exports are XLSX.");
        break;
    case AutomationMode.Wilken:
        Console.WriteLine("MODE: Wilken — attach-only real desktop (Citrix).");
        Console.WriteLine("  1. Open Test Environment from Citrix Workspace and log in yourself.");
        Console.WriteLine("  2. Run this worker inside that desktop (same Windows session as Wilken).");
        Console.WriteLine("  3. Inspect first: dotnet run --project WilkenAutomation.Worker -- --inspect");
        Console.WriteLine("  4. Map Wilken:Selectors from the dump, then start a run. Worker never launches or logs in.");
        break;
    default:
        Console.WriteLine("MODE: Mock — NO desktop window. Jobs are simulated.");
        Console.WriteLine("To drive the Wilken CS/2 replica instead, run:");
        Console.WriteLine("  dotnet run --project WilkenAutomation.Worker -- --desktop-test");
        break;
}
Console.WriteLine("============================================================");
Console.WriteLine();

var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AutomationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Schema");
    DatabaseSchemaPatcher.ApplyAsync(db, logger).GetAwaiter().GetResult();
}
host.Run();

static void ApplyDesktopTestDefaults(WilkenOptions options)
{
    options.MainWindowTitle = "Wilken_CS/2_Finanzmanagement";
    options.ProcessName = "WilkenCs2ReplicaMock";
    options.AttachOnly = false;
    options.SkipLogin = true;
    options.RequireInspectedSelectors = false;
    if (options.PollingIntervalMs <= 0) options.PollingIntervalMs = 400;

    void Set(string key, string value) => options.Selectors[key] = value;

    // Replica has no login screen.
    Set("LoginUsername", "");
    Set("LoginPassword", "");
    Set("LoginButton", "");
    Set("ExecuteButton", "AutomationId:Toolbar_Execute");
    Set("SpoolList", "AutomationId:Spool_Grid");
    Set("KnownDialogTitles", "Zugangsliste|Anlagenspiegel|Fortschritt|Druckauswahl|Gitterbox");
    Set("DismissibleButtonNames", "OK|Close|Weiter");
}

static void ResolveDesktopTestExecutable(WilkenOptions options, string contentRoot)
{
    options.ProcessName = string.IsNullOrWhiteSpace(options.ProcessName)
        ? "WilkenCs2ReplicaMock"
        : options.ProcessName;

    if (!string.IsNullOrWhiteSpace(options.ExecutablePath) && File.Exists(options.ExecutablePath))
        return;

    var replicaRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "WilkenCs2ReplicaMock", "src", "WilkenCs2ReplicaMock"));
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "Replica", "WilkenCs2ReplicaMock.exe"),
        Path.Combine(AppContext.BaseDirectory, "WilkenCs2ReplicaMock.exe"),
        Path.Combine(replicaRoot, "bin", "Debug", "net8.0-windows", "WilkenCs2ReplicaMock.exe"),
        Path.Combine(replicaRoot, "bin", "Release", "net8.0-windows", "WilkenCs2ReplicaMock.exe"),
        Path.Combine(AppContext.BaseDirectory, "WilkenAutomation.TestDesktop.exe"),
        Path.GetFullPath(Path.Combine(contentRoot, "..", "WilkenAutomation.TestDesktop", "bin", "Debug", "net8.0-windows", "WilkenAutomation.TestDesktop.exe")),
        Path.GetFullPath(Path.Combine(contentRoot, "..", "WilkenAutomation.TestDesktop", "bin", "Release", "net8.0-windows", "WilkenAutomation.TestDesktop.exe"))
    };

    var found = candidates.FirstOrDefault(File.Exists);
    if (found is not null)
        options.ExecutablePath = found;
}
