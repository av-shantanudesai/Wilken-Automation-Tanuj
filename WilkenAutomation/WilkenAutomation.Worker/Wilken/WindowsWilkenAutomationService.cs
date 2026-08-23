using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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
/// Combo selection and dialog handling live in companion partial files.
/// </summary>
public partial class WindowsWilkenAutomationService : IWilkenAutomationService, IDisposable
{
    private readonly WilkenOptions _options;
    private readonly IWilkenCredentialProvider _credentials;
    private readonly ExportSettings _exportSettings;
    private readonly ILogger<WindowsWilkenAutomationService> _logger;

    private UIA3Automation? _automation;
    private FlaUI.Core.Application? _app;
    private Window? _mainWindow;

    private ExportJob? _job;
    private RunConfig? _runConfig;
    private DateTime _runStartedAtUtc;
    private readonly HashSet<string> _spoolSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExportDefinitionCatalog _catalog;

    public WilkenSessionStatus SessionStatus { get; private set; } = WilkenSessionStatus.NotRunning;

    private bool IsReplica =>
        (_options.ProcessName ?? "").Contains("WilkenCs2ReplicaMock", StringComparison.OrdinalIgnoreCase)
        || (_options.MainWindowTitle ?? "").Contains("Wilken_CS/2", StringComparison.OrdinalIgnoreCase);

    public WindowsWilkenAutomationService(
        WilkenOptions options,
        IWilkenCredentialProvider credentials,
        ExportSettings exportSettings,
        ExportDefinitionCatalog catalog,
        ILogger<WindowsWilkenAutomationService> logger)
    {
        _options = options;
        _credentials = credentials;
        _exportSettings = exportSettings;
        _catalog = catalog;
        _logger = logger;
    }

    public Task BeginJobAsync(ExportJob job, RunConfig config, CancellationToken ct)
    {
        _job = job;
        _runConfig = config;
        _runStartedAtUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (TryIsAlive() && SessionStatus == WilkenSessionStatus.Ready)
            return;

        SessionStatus = WilkenSessionStatus.Starting;
        _automation ??= new UIA3Automation();

        var processName = EffectiveProcessName();
        var existing = GetWilkenProcesses(processName);
        var launched = false;
        if (existing.Length > 0)
        {
            _app = FlaUI.Core.Application.Attach(existing[0]);
            _logger.LogInformation("Attached to running Wilken process {Pid}.", existing[0].Id);
            if (FindMainWindow() is null)
            {
                _logger.LogWarning("Wilken process {Pid} has no main window (user closed it). Restarting.", existing[0].Id);
                await KillTrackedProcessAsync();
                LaunchWilken();
                launched = true;
            }
        }
        else
        {
            LaunchWilken();
            launched = true;
        }

        await WaitHelper.WaitUntilAsync(() =>
        {
            _mainWindow = FindMainWindow();
            return _mainWindow is not null;
        },
        TimeSpan.FromSeconds(_options.StartupTimeoutSeconds),
        TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
        $"Wilken main window '{_options.MainWindowTitle}'", ct);

        if (launched && !IsReplica)
            MinimizeWithoutActivating();

        HandleDialogs();
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

        SetControlValue(userBox, credentials.Username);
        SetControlValue(Find("LoginPassword"), credentials.Password);
        InvokeControl(Find("LoginButton"));
        _logger.LogInformation("Login submitted for user (name not logged).");

        await WaitUntilUiAsync(
            () => TryFind("LoginUsername") is null,
            TimeSpan.FromSeconds(_options.LoginTimeoutSeconds),
            "login completion", ct);
    }

    public async Task SelectClientAsync(string client, CancellationToken ct)
    {
        if (IsReplica)
        {
            GuardHealthy();
            return;
        }
        GuardHealthy();
        HandleDialogs();
        await SetSelectorValueAsync("ClientField", client, ct);
    }

    public Task CaptureSpoolSnapshotAsync(CancellationToken ct)
    {
        if (IsReplica)
            ReplicaCaptureSpoolSnapshot();
        return Task.CompletedTask;
    }

    public async Task OpenExportDefinitionAsync(string definitionName, CancellationToken ct)
    {
        if (IsReplica)
        {
            await ReplicaOpenReportAsync(definitionName, ct);
            return;
        }

        await OpenAssetAccountingAsync(ct);
    }

    public async Task OpenAssetAccountingAsync(CancellationToken ct)
    {
        if (IsReplica)
        {
            await ReplicaOpenReportAsync(_job?.ExportDefinition, ct);
            return;
        }
        GuardHealthy();
        HandleDialogs();
        InvokeControl(Find("AssetAccountingMenu"));
        await WaitUntilUiAsync(
            () => TryFind("AssetAccountingWindowMarker") is not null,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            "Asset Accounting window", ct);
    }

    public async Task SetFiscalYearAsync(int fiscalYear, CancellationToken ct)
    {
        if (IsReplica)
        {
            await ReplicaSetPeriodAsync(fiscalYear, ct);
            return;
        }
        GuardHealthy();
        HandleDialogs();
        var field = Find("FiscalYearField");
        SetControlValue(field, fiscalYear.ToString());
        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFind("FiscalYearField"), fiscalYear.ToString()),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"fiscal year '{fiscalYear}'", ct);
    }

    public async Task SelectDepartmentAsync(string department, CancellationToken ct)
    {
        if (IsReplica)
        {
            await ReplicaSetFachbereichAsync(department, ct);
            return;
        }
        GuardHealthy();
        HandleDialogs();
        await SetSelectorValueAsync("DepartmentField", department, ct);
    }

    public async Task StartEvaluationAsync(CancellationToken ct)
    {
        if (IsReplica)
        {
            await ReplicaExecuteAsync(ct);
            return;
        }
        GuardHealthy();
        HandleDialogs();
        TryCollapse(TryFind("ClientField"));
        TryCollapse(TryFind("DepartmentField"));
        InvokeControl(Find("ExecuteButton"));
        SessionStatus = WilkenSessionStatus.Busy;
    }

    public async Task WaitForReportReadyAsync(CancellationToken ct)
    {
        if (IsReplica)
        {
            await ReplicaWaitForProgressAsync(ct);
            return;
        }
        var readyText = _options.Selectors.GetValueOrDefault("ReportReadyText", "");
        await WaitUntilUiAsync(() =>
        {
            var indicator = TryFind("ReportStatusIndicator");
            if (indicator is null) return false;
            var value = ReadValue(indicator);
            if (value.Contains("Error", StringComparison.OrdinalIgnoreCase))
                throw new WilkenAutomationException("REPORT_ERROR", $"Wilken reported an evaluation error: {value}");
            return string.IsNullOrEmpty(readyText)
                ? indicator.Properties.IsEnabled.ValueOrDefault
                : value.Contains(readyText, StringComparison.OrdinalIgnoreCase);
        },
        TimeSpan.FromMinutes(_options.ReportTimeoutMinutes),
        "report/spool readiness", ct);
        SessionStatus = WilkenSessionStatus.Ready;
    }

    public async Task OpenSpoolAsync(CancellationToken ct)
    {
        if (IsReplica)
        {
            await ReplicaOpenSpoolAsync(ct);
            return;
        }
        GuardHealthy();
        HandleDialogs();
        InvokeControl(Find("SpoolMenu"));
        await WaitUntilUiAsync(() => SpoolHasReport(),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            "spool list with a generated report", ct);
    }

    public async Task<string> ExportAsync(ExportJob job, CancellationToken ct)
    {
        if (IsReplica)
            return await ReplicaExportAsync(job, ct);

        GuardHealthy();
        HandleDialogs();

        var extension = string.IsNullOrWhiteSpace(_exportSettings.FileExtension) ? ".csv" : _exportSettings.FileExtension;
        if (!extension.StartsWith('.')) extension = "." + extension;
        var tempPath = Path.Combine(Path.GetTempPath(), "WilkenAutomationExports",
            $"{job.JobId}-{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        await WaitUntilUiAsync(() =>
        {
            var export = TryFind("ExportButton");
            return export is not null && export.Properties.IsEnabled.ValueOrDefault;
        }, TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds), "enabled Export button", ct);

        InvokeControl(Find("ExportButton"));

        await WaitUntilUiAsync(
            () => TryFind("SaveDialogFileName") is not null,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            "save dialog", ct);

        var nameBox = Find("SaveDialogFileName");
        SetControlValue(nameBox, tempPath);
        await WaitUntilUiAsync(
            () => ReadValue(Find("SaveDialogFileName")).Contains(tempPath, StringComparison.OrdinalIgnoreCase)
                  || ControlShowsValue(TryFind("SaveDialogFileName"), Path.GetFileName(tempPath)),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            "save path typed", ct);

        InvokeControl(Find("SaveDialogConfirm"));
        _logger.LogInformation("Export confirmed. Expected archive name: Mandant_{Client}_{Year}_{Department}{Ext}",
            job.Client, job.FiscalYear, job.Department, extension);
        return tempPath;
    }

    public Task<bool> IsSessionHealthyAsync(CancellationToken ct)
    {
        var alive = TryIsAlive();
        if (!alive)
            SessionStatus = SessionStatus == WilkenSessionStatus.NotResponding
                ? WilkenSessionStatus.NotResponding
                : WilkenSessionStatus.NotRunning;
        return Task.FromResult(alive);
    }

    public async Task RecoverSessionAsync(CancellationToken ct)
    {
        SessionStatus = WilkenSessionStatus.Recovering;
        _logger.LogWarning("Recovering Wilken session: closing broken instance and restarting.");
        await KillTrackedProcessAsync();
        await EnsureSessionAsync(ct);
    }

    /// <summary>Diagnostics: dump the Wilken control tree for selector mapping.</summary>
    public string DumpControlTree() => UiaTreeDumper.DumpWindowByTitle(_options.MainWindowTitle);

    /// <summary>
    /// Raise a control's action through UIA patterns only. Never uses mouse Click,
    /// which would steal the cursor and bring the window to the foreground.
    /// </summary>
    private void InvokeControl(AutomationElement element)
    {
        WithoutStealingInput(() =>
        {
            try
            {
                if (element.Patterns.Invoke.IsSupported)
                {
                    element.Patterns.Invoke.Pattern.Invoke();
                    return;
                }
            }
            catch
            {
                // WPF sometimes exposes Invoke but fails to raise the routed event.
            }

            if (element.ControlType == ControlType.MenuItem)
            {
                element.AsMenuItem().Invoke();
                return;
            }

            try
            {
                element.AsButton().Invoke();
                return;
            }
            catch
            {
                // Fall through to a clear error.
            }

            throw new WilkenAutomationException("CONTROL_INVOKE_FAILED",
                $"Control '{element.AutomationId ?? element.Name}' does not support UIA Invoke. Mouse clicks are disabled.");
        });
    }

    private void SetControlValue(AutomationElement element, string value)
    {
        WithoutStealingInput(() =>
        {
            try
            {
                if (element.Patterns.Value.IsSupported && !element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault)
                {
                    element.Patterns.Value.Pattern.SetValue(value);
                    return;
                }
            }
            catch
            {
                // Try editable combo below.
            }

            try
            {
                var combo = element.AsComboBox();
                if (combo.IsEditable)
                {
                    combo.EditableText = value;
                    return;
                }
            }
            catch
            {
                // Not a combo.
            }

            throw new WilkenAutomationException("VALUE_PATTERN_UNSUPPORTED",
                $"Control '{element.AutomationId ?? element.Name}' cannot be set via UIA Value pattern. Keyboard typing is disabled.");
        });
    }

    private void WithoutStealingInput(Action action)
    {
        using (UserInputGuard.Capture(_app?.ProcessId ?? 0))
            action();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SwShowMinNoActivate = 7;

    /// <summary>
    /// Used only after we launch the process. Never re-minimize afterwards —
    /// the user must be able to restore the window from the taskbar to watch.
    /// </summary>
    private void MinimizeWithoutActivating()
    {
        try
        {
            var hwnd = NativeMainWindowHandle();
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SwShowMinNoActivate);
        }
        catch
        {
            // Best effort; never fail a job because of z-order.
        }
    }

    private IntPtr NativeMainWindowHandle()
    {
        try
        {
            _mainWindow = FindMainWindow() ?? _mainWindow;
            if (_mainWindow is null) return IntPtr.Zero;
            var handle = _mainWindow.Properties.NativeWindowHandle.ValueOrDefault;
            return handle == 0 ? IntPtr.Zero : new IntPtr(handle);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private void GuardHealthy()
    {
        if (!TryIsAlive())
            throw new WilkenAutomationException("WILKEN_NOT_RUNNING",
                "Wilken desktop was closed or the process is no longer running. Session will be recovered and the job retried.",
                sessionLost: true);
    }

    private Task WaitUntilUiAsync(Func<bool> condition, TimeSpan timeout, string description, CancellationToken ct)
        => WaitHelper.WaitUntilAsync(() =>
        {
            try
            {
                ThrowIfSessionLost();
                HandleDialogs();
                return condition();
            }
            catch (WilkenAutomationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                throw new WilkenAutomationException("WILKEN_NOT_RUNNING",
                    "Wilken UI became unavailable (window closed or process died).",
                    sessionLost: true, ex);
            }
        }, timeout, TimeSpan.FromMilliseconds(_options.PollingIntervalMs), description, ct);

    private void ThrowIfSessionLost()
    {
        if (!TryIsAlive())
            throw new WilkenAutomationException("WILKEN_NOT_RUNNING",
                "Wilken desktop disappeared while waiting (window closed or process died).",
                sessionLost: true);
    }

    private bool TryIsAlive()
    {
        try
        {
            if (_app is null || _app.HasExited) return false;
            var process = Process.GetProcessById(_app.ProcessId);
            if (process.HasExited) return false;
            if (!process.Responding)
            {
                SessionStatus = WilkenSessionStatus.NotResponding;
                return false;
            }

            _mainWindow = FindMainWindow() ?? _mainWindow;
            if (_mainWindow is null) return false;
            _ = _mainWindow.Title;
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private Window? FindMainWindow()
    {
        if (_app is null || _automation is null) return null;
        try
        {
            return _app.GetAllTopLevelWindows(_automation)
                .FirstOrDefault(w => (w.Title ?? "").Contains(_options.MainWindowTitle, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private string EffectiveProcessName()
    {
        var fromRun = _runConfig?.WilkenExecutablePath?.Trim();
        if (!string.IsNullOrWhiteSpace(fromRun))
            return Path.GetFileNameWithoutExtension(fromRun);
        return string.IsNullOrWhiteSpace(_options.ProcessName)
            ? Path.GetFileNameWithoutExtension(_options.ExecutablePath)
            : _options.ProcessName;
    }

    private static Process[] GetWilkenProcesses(string processName) =>
        string.IsNullOrEmpty(processName) ? Array.Empty<Process>() : Process.GetProcessesByName(processName);

    private void LaunchWilken()
    {
        var exe = EffectiveExecutablePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            throw new WilkenAutomationException("WILKEN_EXE_NOT_FOUND",
                $"Wilken executable not configured or missing: '{exe}'. Set the path on New Run or Wilken:ExecutablePath.",
                sessionLost: true);

        _app = FlaUI.Core.Application.Launch(new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            WindowStyle = IsReplica ? ProcessWindowStyle.Normal : ProcessWindowStyle.Minimized
        });
        _logger.LogInformation(
            IsReplica
                ? "Launched Wilken CS/2 replica ({Path}). Window stays visible; automation uses UIA only (no mouse)."
                : "Launched Wilken CS/2 ({Path}). Starts minimized; restore from the taskbar to watch. Automation uses UIA only (no mouse).",
            exe);
    }

    private string EffectiveExecutablePath()
    {
        var fromRun = _runConfig?.WilkenExecutablePath?.Trim();
        if (!string.IsNullOrWhiteSpace(fromRun))
            return fromRun;
        return _options.ExecutablePath ?? "";
    }

    private async Task KillTrackedProcessAsync()
    {
        try
        {
            if (_app is { HasExited: false })
            {
                _app.Close();
                await Task.Delay(400);
                if (!_app.HasExited) _app.Kill();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error while closing Wilken: {Message}", ex.Message);
        }

        foreach (var process in GetWilkenProcesses(EffectiveProcessName()))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not kill leftover Wilken process {Pid}: {Message}", process.Id, ex.Message);
            }
        }

        _app = null;
        _mainWindow = null;
        SessionStatus = WilkenSessionStatus.NotRunning;
    }

    private bool SpoolHasReport()
    {
        var list = TryFind("SpoolList");
        if (list is null) return false;
        try
        {
            var items = list.AsListBox().Items;
            if (items is { Length: > 0 }) return true;
        }
        catch { }

        try { return list.FindAllChildren().Length > 0; }
        catch { return false; }
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
        if (!_options.Selectors.TryGetValue(selectorKey, out var selector) || string.IsNullOrEmpty(selector))
            return null;

        foreach (var root in SearchRoots())
        {
            var hit = TryFindIn(root, selectorKey);
            if (hit is not null) return hit;
        }
        return null;
    }

    private AutomationElement? TryFindIn(AutomationElement root, string selectorKey)
    {
        if (!_options.Selectors.TryGetValue(selectorKey, out var selector) || string.IsNullOrEmpty(selector))
            return null;

        var parts = selector.Split(':', 2);
        if (parts.Length != 2) return null;
        var (kind, value) = (parts[0].Trim(), parts[1].Trim());

        try
        {
            return kind.ToLowerInvariant() switch
            {
                "automationid" => root.FindFirstDescendant(cf => cf.ByAutomationId(value))
                    ?? (root.AutomationId == value ? root : null),
                "name" => root.FindFirstDescendant(cf => cf.ByName(value)),
                "classname" => root.FindFirstDescendant(cf => cf.ByClassName(value)),
                _ => null
            };
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private IEnumerable<AutomationElement> SearchRoots()
    {
        _mainWindow = FindMainWindow() ?? _mainWindow;
        if (_mainWindow is not null) yield return _mainWindow;
        if (_app is null || _automation is null) yield break;
        Window[] windows;
        try { windows = _app.GetAllTopLevelWindows(_automation); }
        catch { yield break; }
        foreach (var window in windows)
        {
            if (_mainWindow is not null && window.Equals(_mainWindow)) continue;
            yield return window;
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
