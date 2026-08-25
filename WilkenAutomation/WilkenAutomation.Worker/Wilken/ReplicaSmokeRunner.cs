using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// Drives one full Zugangsliste job plus a second job against the replica,
/// using the same FlaUI service the worker uses. Run:
///   dotnet run --project WilkenAutomation.Worker -- --replica-smoke
///   dotnet run --project WilkenAutomation.Worker -- --replica-login-smoke
/// The login smoke launches EHP + Anmeldung and waits for you to click through
/// (same Mandant as the job, e.g. 02) before the export workflow starts.
/// </summary>
internal static class ReplicaSmokeRunner
{
    public static async Task<int> RunAsync(bool manualLogin = false)
    {
        foreach (var process in Process.GetProcessesByName("WilkenCs2ReplicaMock"))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
        }

        var contentRoot = Directory.GetCurrentDirectory();
        var exe = ResolveReplicaExe(contentRoot);
        if (exe is null)
        {
            Console.Error.WriteLine("Replica exe not found.");
            return 2;
        }

        Console.WriteLine("CLI SMOKE — this does NOT use the dashboard. Jobs start immediately.");
        Console.WriteLine("For Generate jobs on the UI, stop this and run:");
        Console.WriteLine("  dotnet run --project WilkenAutomation.Worker --no-launch-profile -- --desktop-test");
        Console.WriteLine($"SMOKE replica: {exe}");
        if (manualLogin)
        {
            Console.WriteLine("MANUAL LOGIN: complete EHP (Start Wilken) then Anmeldung (Anmelden).");
            Console.WriteLine("Use Mandant 02 — same value as the dashboard job. Automation will not type it.");
        }

        var options = new WilkenOptions
        {
            ExecutablePath = exe,
            StartupArguments = manualLogin ? "--manual-login" : "",
            MainWindowTitle = "Wilken_CS/2_Finanzmanagement",
            ProcessName = "WilkenCs2ReplicaMock",
            AttachOnly = false,
            SkipLogin = true,
            RequireInspectedSelectors = false,
            RequireManualLoginScreens = manualLogin,
            ManualLoginTimeoutMinutes = manualLogin ? 15 : 1,
            NavigationTimeoutSeconds = 45,
            ReportTimeoutMinutes = 5,
            ExportTimeoutMinutes = 3,
            PollingIntervalMs = 400,
            Selectors =
            {
                ["ExecuteButton"] = "AutomationId:Toolbar_Execute",
                ["SpoolList"] = "AutomationId:Spool_Grid",
                ["KnownDialogTitles"] = "Zugangsliste|Anlagenspiegel|Fortschritt|Druckauswahl|Gitterbox|Funktion gesperrt",
                ["DismissibleButtonNames"] = "OK|Close|Weiter"
            }
        };

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(o => o.SingleLine = true);
        });
        var catalog = ExportDefinitionCatalog.Load(contentRoot);
        var export = new ExportSettings { FileExtension = ".xlsx" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        using var wilken = new WindowsWilkenAutomationService(
            options,
            new ConfigurationCredentialProvider(config),
            export,
            catalog,
            loggerFactory.CreateLogger<WindowsWilkenAutomationService>());

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(manualLogin ? 20 : 8));
        var runConfig = new RunConfig { EnableContentValidation = false };

        try
        {
            await RunOneAsync(wilken, "smoke-1", cts.Token, runConfig);
            Console.WriteLine("SMOKE first job OK — starting second job (repeat / Liste anzeigen re-entry).");
            await RunOneAsync(wilken, "smoke-2", cts.Token, runConfig);
            Console.WriteLine("SMOKE both jobs succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SMOKE FAILED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            foreach (var process in Process.GetProcessesByName("WilkenCs2ReplicaMock"))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
            }
        }
    }

    private static async Task RunOneAsync(
        WindowsWilkenAutomationService wilken,
        string jobId,
        CancellationToken ct,
        RunConfig runConfig)
    {
        var job = new ExportJob
        {
            JobId = jobId,
            RunId = "smoke",
            Client = "02",
            FiscalYear = 2020,
            Department = "Handelsrecht",
            AccountingLaw = "Handelsrecht",
            ExportDefinition = "Zugangsliste",
            DepartmentCode = "HR"
        };

        Step("BeginJob");
        await wilken.BeginJobAsync(job, runConfig, ct);
        Step("EnsureSession");
        await wilken.EnsureSessionAsync(ct);
        Step("OpenExportDefinition");
        await wilken.OpenExportDefinitionAsync(job.ExportDefinition, ct);
        Step("SetFiscalYear");
        await wilken.SetFiscalYearAsync(job.FiscalYear, ct);
        Step("SelectDepartment");
        await wilken.SelectDepartmentAsync(job.Department, ct);
        Step("CaptureSpoolSnapshot");
        await wilken.CaptureSpoolSnapshotAsync(ct);
        Step("StartEvaluation");
        await wilken.StartEvaluationAsync(ct);
        Step("WaitForReportReady");
        await wilken.WaitForReportReadyAsync(ct);
        Step("OpenSpool");
        await wilken.OpenSpoolAsync(ct);
        Step("Export");
        var path = await wilken.ExportAsync(job, ct);
        Step($"Export file {path}");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Export file missing: {path}");
        Step("ReturnToProcessManager");
        await wilken.ReturnToProcessManagerAsync(ct);
        Step($"{jobId} complete");
    }

    private static void Step(string name) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} SMOKE {name}");

    private static string? ResolveReplicaExe(string contentRoot)
    {
        var replicaRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "WilkenCs2ReplicaMock", "src", "WilkenCs2ReplicaMock"));
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Replica", "WilkenCs2ReplicaMock.exe"),
            Path.Combine(replicaRoot, "bin", "Debug", "net8.0-windows", "WilkenCs2ReplicaMock.exe"),
            Path.Combine(replicaRoot, "bin", "Release", "net8.0-windows", "WilkenCs2ReplicaMock.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
