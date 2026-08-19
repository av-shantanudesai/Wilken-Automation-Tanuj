using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Application.Validators;
using WilkenAutomation.Infrastructure.Configuration;
using WilkenAutomation.Infrastructure.Database;
using WilkenAutomation.Worker.Realtime;
using WilkenAutomation.Worker.Wilken;
using WilkenAutomation.Worker.Windows;
using WilkenAutomation.Worker.Worker;

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

var forceDesktopTest = args.Any(a => a.Equals("--desktop-test", StringComparison.OrdinalIgnoreCase));
var hostArgs = args.Where(a => !a.Equals("--desktop-test", StringComparison.OrdinalIgnoreCase)).ToArray();

var builder = Host.CreateApplicationBuilder(hostArgs);
if (forceDesktopTest)
{
    builder.Configuration.AddJsonFile("appsettings.DesktopTest.json", optional: true, reloadOnChange: false);
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Worker:AutomationMode"] = "DesktopTest",
        ["Wilken:Username"] = "tester",
        ["Wilken:Password"] = "tester",
        ["Wilken:MainWindowTitle"] = "Wilken CS/2 Test Desktop",
        ["Wilken:ProcessName"] = "WilkenAutomation.TestDesktop"
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

var jwtOptions = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
JwtTokenService.EnsureProductionKey(jwtOptions, builder.Environment.EnvironmentName);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(new JwtTokenService(jwtOptions));

builder.Services.AddWilkenInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IWilkenCredentialProvider, ConfigurationCredentialProvider>();
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

Console.WriteLine();
Console.WriteLine("============================================================");
switch (workerSettings.AutomationMode)
{
    case AutomationMode.DesktopTest:
        Console.WriteLine("MODE: DesktopTest — dummy Windows UI will open.");
        Console.WriteLine($"Exe: {wilkenOptions.ExecutablePath}");
        break;
    case AutomationMode.Wilken:
        Console.WriteLine("MODE: Wilken — real Wilken CS/2 desktop automation.");
        break;
    default:
        Console.WriteLine("MODE: Mock — NO desktop window. Jobs are simulated.");
        Console.WriteLine("To open the dummy UI instead, run:");
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
    options.MainWindowTitle = "Wilken CS/2 Test Desktop";
    options.ProcessName = "WilkenAutomation.TestDesktop";
    if (options.PollingIntervalMs <= 0) options.PollingIntervalMs = 400;

    void Set(string key, string value)
    {
        if (!options.Selectors.TryGetValue(key, out var current) || string.IsNullOrWhiteSpace(current))
            options.Selectors[key] = value;
    }

    Set("LoginUsername", "AutomationId:LoginUsername");
    Set("LoginPassword", "AutomationId:LoginPassword");
    Set("LoginButton", "AutomationId:LoginButton");
    Set("ClientField", "AutomationId:ClientField");
    Set("AssetAccountingMenu", "AutomationId:AssetAccountingMenu");
    Set("AssetAccountingWindowMarker", "AutomationId:AssetAccountingWindowMarker");
    Set("FiscalYearField", "AutomationId:FiscalYearField");
    Set("DepartmentField", "AutomationId:DepartmentField");
    Set("ExecuteButton", "AutomationId:ExecuteButton");
    Set("ReportStatusIndicator", "AutomationId:ReportStatusIndicator");
    Set("ReportReadyText", "Ready");
    Set("SpoolMenu", "AutomationId:SpoolMenu");
    Set("SpoolList", "AutomationId:SpoolList");
    Set("ExportButton", "AutomationId:ExportButton");
    Set("SaveDialogFileName", "AutomationId:SaveDialogFileName");
    Set("SaveDialogConfirm", "AutomationId:SaveDialogConfirm");
    Set("UnexpectedDialog", "AutomationId:UnexpectedDialog");
    Set("DialogOkButton", "AutomationId:DialogOkButton");
    Set("KnownDialogTitles", "Save export");
    Set("DismissibleButtonNames", "OK|Close|Ja|Yes|Weiter");
}

static void ResolveDesktopTestExecutable(WilkenOptions options, string contentRoot)
{
    options.ProcessName = string.IsNullOrWhiteSpace(options.ProcessName)
        ? "WilkenAutomation.TestDesktop"
        : options.ProcessName;

    if (!string.IsNullOrWhiteSpace(options.ExecutablePath) && File.Exists(options.ExecutablePath))
        return;

    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "WilkenAutomation.TestDesktop.exe"),
        Path.GetFullPath(Path.Combine(contentRoot, "..", "WilkenAutomation.TestDesktop", "bin", "Debug", "net8.0-windows", "WilkenAutomation.TestDesktop.exe")),
        Path.GetFullPath(Path.Combine(contentRoot, "..", "WilkenAutomation.TestDesktop", "bin", "Release", "net8.0-windows", "WilkenAutomation.TestDesktop.exe"))
    };

    var found = candidates.FirstOrDefault(File.Exists);
    if (found is not null)
        options.ExecutablePath = found;
}
