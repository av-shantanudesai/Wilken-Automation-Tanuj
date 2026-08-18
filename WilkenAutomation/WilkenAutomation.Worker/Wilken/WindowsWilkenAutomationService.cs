using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Worker.Windows;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// Real Wilken CS/2 desktop automation (AutomationMode=Wilken), built on Windows
/// UI Automation via FlaUI (UIA3). Runs in the interactive Windows session.
///
/// Interaction priority: UIA control identification (AutomationId/Name/ClassName)
/// first; keyboard/image/coordinates are deliberate later fallbacks and never the
/// primary mechanism. Every workflow step resolves its control through the
/// Wilken:Selectors configuration produced by the control-discovery POC
/// (dotnet run -- --inspect "Wilken"). Unmapped steps fail with CONTROL_NOT_MAPPED
/// instead of blind clicking - per specification the real workflow is wired up
/// only after the actual Wilken controls have been inspected.
/// </summary>
public class WindowsWilkenAutomationService : IWilkenAutomationService, IDisposable
{
    private readonly WilkenOptions _options;
    private readonly IWilkenCredentialProvider _credentials;
    private readonly ILogger<WindowsWilkenAutomationService> _logger;

    private UIA3Automation? _automation;
    private FlaUI.Core.Application? _app;
    private Window? _mainWindow;

    public WilkenSessionStatus SessionStatus { get; private set; } = WilkenSessionStatus.NotRunning;

    public WindowsWilkenAutomationService(
        WilkenOptions options,
        IWilkenCredentialProvider credentials,
        ILogger<WindowsWilkenAutomationService> logger)
    {
        _options = options;
        _credentials = credentials;
        _logger = logger;
    }

    public Task BeginJobAsync(ExportJob job, RunConfig config, CancellationToken ct) => Task.CompletedTask;

    public async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (_app is { HasExited: false } && _mainWindow is not null && SessionStatus == WilkenSessionStatus.Ready)
            return;

        SessionStatus = WilkenSessionStatus.Starting;
        _automation ??= new UIA3Automation();

        // Prefer attaching to an already running Wilken instance.
        var processName = _options.ProcessName
            ?? Path.GetFileNameWithoutExtension(_options.ExecutablePath);
        var existing = string.IsNullOrEmpty(processName) ? Array.Empty<Process>() : Process.GetProcessesByName(processName);
        if (existing.Length > 0)
        {
            _app = FlaUI.Core.Application.Attach(existing[0]);
            _logger.LogInformation("Attached to running Wilken process {Pid}.", existing[0].Id);
        }
        else
        {
            if (string.IsNullOrEmpty(_options.ExecutablePath) || !File.Exists(_options.ExecutablePath))
                throw new WilkenAutomationException("WILKEN_EXE_NOT_FOUND",
                    $"Wilken executable not configured or missing: '{_options.ExecutablePath}'. Set Wilken:ExecutablePath.",
                    sessionLost: true);

            _app = FlaUI.Core.Application.Launch(_options.ExecutablePath);
            _logger.LogInformation("Launched Wilken CS/2 ({Path}).", _options.ExecutablePath);
        }

        // State-based startup wait: main window must appear within the configured timeout.
        await WaitHelper.WaitUntilAsync(() =>
        {
            _mainWindow = _app.GetAllTopLevelWindows(_automation)
                .FirstOrDefault(w => (w.Title ?? "").Contains(_options.MainWindowTitle, StringComparison.OrdinalIgnoreCase));
            return _mainWindow is not null;
        },
        TimeSpan.FromSeconds(_options.StartupTimeoutSeconds),
        TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
        $"Wilken main window '{_options.MainWindowTitle}'", ct);

        await LoginIfRequiredAsync(ct);
        SessionStatus = WilkenSessionStatus.Ready;
    }

    private async Task LoginIfRequiredAsync(CancellationToken ct)
    {
        var userSelector = _options.Selectors.GetValueOrDefault("LoginUsername");
        if (string.IsNullOrEmpty(userSelector)) return; // no login mapped -> assume session without login

        var userBox = TryFind("LoginUsername");
        if (userBox is null) return; // login screen not shown

        SessionStatus = WilkenSessionStatus.LoginRequired;
        var credentials = await _credentials.GetCredentialsAsync(ct)
            ?? throw new WilkenAutomationException("CREDENTIALS_MISSING",
                "Wilken login screen detected but no credentials are configured (Wilken:Username / WILKEN_PASSWORD).");

        userBox.AsTextBox().Text = credentials.Username;
        Find("LoginPassword").AsTextBox().Text = credentials.Password;
        Find("LoginButton").AsButton().Invoke();
        _logger.LogInformation("Login submitted for user (name not logged).");

        await WaitHelper.WaitUntilAsync(
            () => TryFind("LoginUsername") is null,
            TimeSpan.FromSeconds(_options.LoginTimeoutSeconds),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            "login completion", ct);
    }

    public async Task SelectClientAsync(string client, CancellationToken ct)
    {
        GuardHealthy();
        CheckForUnexpectedDialog();
        var field = Find("ClientField");
        if (field.Patterns.Value.IsSupported) field.AsTextBox().Text = client;
        else field.AsComboBox().Select(client);

        // Verify the selection actually took effect before continuing.
        await WaitHelper.WaitUntilAsync(
            () => ReadValue(field).Contains(client, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            $"client selection '{client}'", ct);
    }

    public async Task OpenAssetAccountingAsync(CancellationToken ct)
    {
        GuardHealthy();
        CheckForUnexpectedDialog();
        Find("AssetAccountingMenu").AsMenuItem().Invoke();
        await WaitHelper.WaitUntilAsync(
            () => TryFind("AssetAccountingWindowMarker") is not null,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            "Asset Accounting window", ct);
    }

    public async Task SetFiscalYearAsync(int fiscalYear, CancellationToken ct)
    {
        GuardHealthy();
        var field = Find("FiscalYearField");
        field.AsTextBox().Text = fiscalYear.ToString();
        await WaitHelper.WaitUntilAsync(
            () => ReadValue(field).Contains(fiscalYear.ToString()),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            $"fiscal year '{fiscalYear}'", ct);
    }

    public async Task SelectDepartmentAsync(string department, CancellationToken ct)
    {
        GuardHealthy();
        var field = Find("DepartmentField");
        field.AsComboBox().Select(department);
        await WaitHelper.WaitUntilAsync(
            () => ReadValue(field).Contains(department, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            $"department '{department}'", ct);
    }

    public Task StartEvaluationAsync(CancellationToken ct)
    {
        GuardHealthy();
        CheckForUnexpectedDialog();
        Find("ExecuteButton").AsButton().Invoke();
        SessionStatus = WilkenSessionStatus.Busy;
        return Task.CompletedTask;
    }

    public async Task WaitForReportReadyAsync(CancellationToken ct)
    {
        // State-based readiness detection - no fixed waits. The indicator element and
        // its ready text come from the control-discovery POC.
        var readyText = _options.Selectors.GetValueOrDefault("ReportReadyText", "");
        await WaitHelper.WaitUntilAsync(() =>
        {
            CheckForUnexpectedDialog();
            var indicator = TryFind("ReportStatusIndicator");
            if (indicator is null) return false;
            var value = ReadValue(indicator);
            return string.IsNullOrEmpty(readyText)
                ? indicator.Properties.IsEnabled.ValueOrDefault
                : value.Contains(readyText, StringComparison.OrdinalIgnoreCase);
        },
        TimeSpan.FromMinutes(_options.ReportTimeoutMinutes),
        TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
        "report/spool readiness", ct);
        SessionStatus = WilkenSessionStatus.Ready;
    }

    public async Task OpenSpoolAsync(CancellationToken ct)
    {
        GuardHealthy();
        Find("SpoolMenu").AsMenuItem().Invoke();
        await WaitHelper.WaitUntilAsync(
            () => TryFind("SpoolList") is not null,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            "spool list", ct);
    }

    public async Task<string> ExportAsync(ExportJob job, CancellationToken ct)
    {
        GuardHealthy();
        CheckForUnexpectedDialog();

        var tempPath = Path.Combine(Path.GetTempPath(), "WilkenAutomationExports",
            $"{job.JobId}-{Guid.NewGuid():N}{".xlsx"}");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        Find("ExportButton").AsButton().Invoke();

        // Save dialog: type the target path, confirm.
        await WaitHelper.WaitUntilAsync(
            () => TryFind("SaveDialogFileName") is not null,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            "save dialog", ct);

        Find("SaveDialogFileName").AsTextBox().Text = tempPath;
        Find("SaveDialogConfirm").AsButton().Invoke();
        return tempPath;
    }

    public Task<bool> IsSessionHealthyAsync(CancellationToken ct)
    {
        if (_app is null || _app.HasExited || _mainWindow is null)
        {
            SessionStatus = WilkenSessionStatus.NotRunning;
            return Task.FromResult(false);
        }
        try
        {
            var process = Process.GetProcessById(_app.ProcessId);
            if (!process.Responding)
            {
                SessionStatus = WilkenSessionStatus.NotResponding;
                return Task.FromResult(false);
            }
        }
        catch
        {
            SessionStatus = WilkenSessionStatus.NotRunning;
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    public async Task RecoverSessionAsync(CancellationToken ct)
    {
        SessionStatus = WilkenSessionStatus.Recovering;
        _logger.LogWarning("Recovering Wilken session: closing broken instance and restarting.");
        try
        {
            if (_app is { HasExited: false })
            {
                _app.Close();                       // graceful close first
                if (!_app.HasExited) _app.Kill();   // force only when required
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error while closing Wilken: {Message}", ex.Message);
        }
        _app = null;
        _mainWindow = null;
        await EnsureSessionAsync(ct);
    }

    /// <summary>Diagnostics: dump the Wilken control tree for selector mapping.</summary>
    public string DumpControlTree() => UiaTreeDumper.DumpWindowByTitle(_options.MainWindowTitle);

    // ------------------------------------------------------------- helpers ----

    private void GuardHealthy()
    {
        if (_app is null || _app.HasExited)
            throw new WilkenAutomationException("WILKEN_NOT_RUNNING",
                "Wilken process is not running.", sessionLost: true);
    }

    /// <summary>
    /// Unknown modal dialogs abort the attempt safely (screenshot + recovery are
    /// handled by the executor) - never random Enter/Escape presses.
    /// </summary>
    private void CheckForUnexpectedDialog()
    {
        if (_mainWindow is null) return;
        Window[] modals;
        try { modals = _mainWindow.ModalWindows; }
        catch { return; }

        foreach (var modal in modals)
        {
            var title = modal.Title ?? "<untitled>";
            var known = _options.Selectors.GetValueOrDefault("KnownDialogTitles", "")
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (known.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;

            throw new WilkenAutomationException("UNEXPECTED_DIALOG",
                $"Unexpected modal dialog detected: '{title}'. Aborting attempt for safe recovery.");
        }
    }

    private AutomationElement Find(string selectorKey) =>
        TryFind(selectorKey) ?? throw new WilkenAutomationException(
            selectorKey == "ClientField" || _options.Selectors.ContainsKey(selectorKey)
                ? "CONTROL_NOT_FOUND"
                : "CONTROL_NOT_MAPPED",
            _options.Selectors.ContainsKey(selectorKey)
                ? $"Control '{selectorKey}' ({_options.Selectors[selectorKey]}) not found in the Wilken UI."
                : $"No selector configured for '{selectorKey}'. Run the control-discovery POC " +
                  $"(dotnet run -- --inspect \"{_options.MainWindowTitle}\") and map Wilken:Selectors:{selectorKey}.");

    private AutomationElement? TryFind(string selectorKey)
    {
        if (_mainWindow is null) return null;
        if (!_options.Selectors.TryGetValue(selectorKey, out var selector) || string.IsNullOrEmpty(selector))
            return null;

        var parts = selector.Split(':', 2);
        if (parts.Length != 2) return null;
        var (kind, value) = (parts[0].Trim(), parts[1].Trim());

        try
        {
            return kind.ToLowerInvariant() switch
            {
                "automationid" => _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(value)),
                "name" => _mainWindow.FindFirstDescendant(cf => cf.ByName(value)),
                "classname" => _mainWindow.FindFirstDescendant(cf => cf.ByClassName(value)),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ReadValue(AutomationElement element)
    {
        try
        {
            if (element.Patterns.Value.IsSupported)
                return element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
            return element.Properties.Name.ValueOrDefault ?? "";
        }
        catch
        {
            return "";
        }
    }

    public void Dispose()
    {
        _automation?.Dispose();
    }
}
