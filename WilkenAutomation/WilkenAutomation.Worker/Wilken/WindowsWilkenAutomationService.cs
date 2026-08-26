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
/// UI Automation via FlaUI (UIA3). Job steps follow the WilkenCs2ReplicaMock CS/2
/// workflow. Replica vs real Wilken only changes launch vs attach.
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

    /// <summary>
    /// Replica exe vs real Citrix Wilken. Session launch/attach only.
    /// Job steps always use the CS/2 workflow (same as WilkenCs2ReplicaMock).
    /// </summary>
    private bool IsReplica =>
        (_options.ProcessName ?? "").Contains("WilkenCs2ReplicaMock", StringComparison.OrdinalIgnoreCase)
        || EffectiveExecutablePath().Contains("WilkenCs2ReplicaMock", StringComparison.OrdinalIgnoreCase);

    /// <summary>Prozesse verwalten → save → execute → Liste anzeigen → spool → Gitterbox → internal close.</summary>
    private bool UseCs2Workflow => true;

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

        EnsureInspectedSelectors();
        if (UseCs2Workflow && !IsReplica)
        {
            var missingCs2 = WilkenSelectorCatalog.MissingCs2(_options.Selectors);
            if (missingCs2.Count > 0)
            {
                _logger.LogWarning(
                    "CS/2 Wilken:Selectors still empty ({Count}): {Keys}. " +
                    "On Test Wilken run --inspect-watch --inspect-record (click every German control), then --apply-selectors.",
                    missingCs2.Count, string.Join(", ", missingCs2));
            }
            else
            {
                _logger.LogInformation("All CS/2 Wilken:Selectors are filled from inspect.");
            }
        }
        if (UseCs2Workflow)
        {
            _logger.LogInformation(
                "CS/2 workflow: Prozesse verwalten → save → Ausführen → Liste anzeigen → spool → Gitterbox-Export → InternalWindow_Close. " +
                "Controls resolve by AutomationId, then Wilken:Selectors, then German names.");
        }

        SessionStatus = WilkenSessionStatus.Starting;
        _automation ??= new UIA3Automation();

        var attachOnly = _options.AttachOnly && !IsReplica;
        var launched = false;

        if (attachOnly)
        {
            _logger.LogInformation(
                "Attach-only Wilken session: waiting for a user-opened desktop (Citrix). Will not launch or kill Wilken.");
            try
            {
                await WaitHelper.WaitUntilAsync(
                    TryAttachToOpenSession,
                    _options.SkipLogin
                        ? TimeSpan.FromMinutes(Math.Max(1, _options.ManualLoginTimeoutMinutes))
                        : TimeSpan.FromSeconds(_options.StartupTimeoutSeconds),
                    TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
                    $"user-opened Wilken window '{_options.MainWindowTitle}'",
                    ct);
            }
            catch (WaitTimeoutException ex)
            {
                throw new WilkenAutomationException("WILKEN_NOT_ATTACHED",
                    "Wilken desktop was not found in this Windows session. " + WilkenSessionPolicy.AttachInstructions,
                    sessionLost: true, ex);
            }
        }
        else
        {
            var processName = EffectiveProcessName();
            if (RequiresManualLoginScreens)
            {
                // Never attach to an already-open main window — that skips EHP/Anmeldung.
                KillProcessesByName(processName);
                _app = null;
                _mainWindow = null;
                await Task.Delay(400, ct);
                LaunchWilken();
                launched = true;
            }
            else
            {
                var existing = GetWilkenProcesses(processName);
                if (existing.Length > 0)
                {
                    AttachToProcess(existing[0]);
                    if (FindMainWindow() is null && FirstTopLevelWindow() is null)
                    {
                        _logger.LogWarning("Wilken process {Pid} has no window (user closed it). Restarting.", existing[0].Id);
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
            }

            await WaitHelper.WaitUntilAsync(() =>
            {
                _mainWindow = FindMainWindow() ?? FirstTopLevelWindow();
                return _app is not null && !_app.HasExited;
            },
            TimeSpan.FromSeconds(_options.StartupTimeoutSeconds),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            "Wilken process started", ct);
        }

        LogAttachedWindow();

        if (launched && !IsReplica)
            MinimizeWithoutActivating();

        if (!_options.SkipLogin)
            await LoginIfRequiredAsync(ct);
        else
            await WaitUntilUserReachedMainScreenAsync(ct);

        HandleDialogs();
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

    /// <summary>
    /// Citrix / replica --manual-login: do not type Country, Company, Benutzer,
    /// Passwort, or Mandant. Wait until the user reaches the main work area
    /// (Navigation / Prozesse verwalten). Dashboard Mandant is job identity only.
    /// </summary>
    private async Task WaitUntilUserReachedMainScreenAsync(CancellationToken ct)
    {
        SessionStatus = WilkenSessionStatus.LoginRequired;
        var timeout = TimeSpan.FromMinutes(Math.Max(1, _options.ManualLoginTimeoutMinutes));

        if (RequiresManualLoginScreens)
        {
            _logger.LogInformation(
                "Manual login: waiting for EHP (Start Wilken) or Anmeldung to appear. Jobs will not start yet.");
            try
            {
                await WaitUntilUiAsync(
                    AnyLoginOrStartupWindowOpen,
                    TimeSpan.FromSeconds(Math.Max(20, _options.StartupTimeoutSeconds)),
                    "EHP or Anmeldung window",
                    ct);
            }
            catch (WaitTimeoutException ex)
            {
                throw new WilkenAutomationException("WILKEN_LOGIN_SCREEN_MISSING",
                    "EHP/Anmeldung never appeared. The replica must start with --manual-login (and WILKEN_REPLICA_MANUAL_LOGIN=1). Close leftover WilkenCs2ReplicaMock windows and retry.",
                    sessionLost: true, ex);
            }

            _logger.LogInformation(
                "Login screens are visible. Waiting up to {Minutes} min for you to click Start Wilken, then Anmelden. Automation does not fill those fields.",
                timeout.TotalMinutes);
        }
        else
        {
            _logger.LogInformation(
                "Waiting up to {Minutes} min for you to finish EHP (Country/Company → Start Wilken) and Anmeldung (Benutzer, Passwort, Mandant → Anmelden). Automation does not fill those fields.",
                timeout.TotalMinutes);
        }

        try
        {
            await WaitUntilUiAsync(
                IsLoggedInMainScreen,
                timeout,
                "logged-in Wilken main screen (Navigation)",
                ct);
        }
        catch (WaitTimeoutException ex)
        {
            throw new WilkenAutomationException("WILKEN_LOGIN_NOT_COMPLETED",
                "Wilken main screen was not reached. Complete EHP and Anmeldung (same Mandant as the dashboard job) and leave the Navigation tree visible.",
                sessionLost: true, ex);
        }

        _logger.LogInformation("User finished login. Main screen is ready; process automation can start.");
        SessionStatus = WilkenSessionStatus.Ready;
    }

    private bool RequiresManualLoginScreens =>
        _options.RequireManualLoginScreens
        || (_options.StartupArguments ?? "").Contains("manual-login", StringComparison.OrdinalIgnoreCase);

    private bool AnyLoginOrStartupWindowOpen()
    {
        if (_app is not null && _automation is not null)
        {
            try
            {
                foreach (var window in _app.GetAllTopLevelWindows(_automation))
                {
                    if (IsUserLoginOrStartupWindow(window))
                        return true;
                }
            }
            catch { }
        }

        return TryFindByAutomationId("Ehp_StartWilken") is not null
            || TryFindByAutomationId("Login_Anmelden") is not null
            || TryFindByAutomationId("EhpStartupDialog") is not null
            || TryFindByAutomationId("LoginDialog") is not null;
    }

    private static bool IsStartupOrLoginTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        return title.Contains("Anmeldung", StringComparison.OrdinalIgnoreCase)
            || title.Contains("EHP 2", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Workspace Environment", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLoggedInMainScreen()
    {
        _mainWindow = FindMainWindow() ?? FirstTopLevelWindow();
        if (_mainWindow is null) return false;

        if (AnyLoginOrStartupWindowOpen()) return false;
        if (IsStartupOrLoginTitle(_mainWindow.Title)) return false;

        if (TryFindByAutomationId("Navigation_Tree") is not null) return true;
        if (TryFindByAutomationId("ProcessManager_Grid") is not null) return true;
        if (TryFindByAutomationId("Home_Workspace") is not null) return true;

        var title = _mainWindow.Title ?? "";
        // After real Anmeldung the shell title is like "1/02 - … - Wilken_CS/2_Finanzmanagement".
        return title.Contains('/')
            && title.Contains("Wilken", StringComparison.OrdinalIgnoreCase)
            && !title.Contains("Anmeldung", StringComparison.OrdinalIgnoreCase);
    }

    public async Task SelectClientAsync(string client, CancellationToken ct)
    {
        // Mandant is chosen by the user on Anmeldung. The dashboard value is only
        // the job identity and must match that login; it is never typed into Wilken.
        if (IsReplica || _options.SkipLogin)
        {
            GuardHealthy();
            _logger.LogInformation(
                "Mandant '{Client}' comes from the dashboard job only. Wilken login Mandant is filled by the user.",
                client);
            return;
        }

        GuardHealthy();
        HandleDialogs();
        await SetSelectorValueAsync("ClientField", client, ct);
    }

    public Task CaptureSpoolSnapshotAsync(CancellationToken ct)
    {
        if (UseCs2Workflow)
            ReplicaCaptureSpoolSnapshot();
        return Task.CompletedTask;
    }

    public async Task OpenExportDefinitionAsync(string definitionName, CancellationToken ct)
    {
        if (UseCs2Workflow)
        {
            await ReplicaOpenReportAsync(definitionName, ct);
            return;
        }

        await OpenAssetAccountingAsync(ct);
    }

    public async Task OpenAssetAccountingAsync(CancellationToken ct)
    {
        if (UseCs2Workflow)
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
        if (UseCs2Workflow)
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
        if (UseCs2Workflow)
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
        if (UseCs2Workflow)
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
        if (UseCs2Workflow)
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
        if (UseCs2Workflow)
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
        if (UseCs2Workflow)
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

    public async Task ReturnToProcessManagerAsync(CancellationToken ct)
    {
        if (UseCs2Workflow)
            await ReplicaReturnToProcessManagerAsync(ct);
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
        if (_options.AttachOnly && !IsReplica)
        {
            _logger.LogWarning(
                "Attach-only recovery: not closing Wilken. Re-attach after the user reopens it from Citrix if needed.");
            _app = null;
            _mainWindow = null;
            SessionStatus = WilkenSessionStatus.NotRunning;
            await EnsureSessionAsync(ct);
            return;
        }

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

            try
            {
                if (element.Patterns.LegacyIAccessible.IsSupported)
                {
                    element.Patterns.LegacyIAccessible.Pattern.DoDefaultAction();
                    return;
                }
            }
            catch
            {
                // Win32/Java MSAA fallback failed.
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

            try
            {
                if (element.Patterns.LegacyIAccessible.IsSupported)
                {
                    element.Patterns.LegacyIAccessible.Pattern.SetValue(value);
                    return;
                }
            }
            catch
            {
                // Win32/Java MSAA fallback failed.
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
                _options.AttachOnly && !IsReplica
                    ? "Wilken desktop was closed or is not in this session. " + WilkenSessionPolicy.AttachInstructions
                    : "Wilken desktop was closed or the process is no longer running. Session will be recovered and the job retried.",
                sessionLost: true);
    }

    private static string SafeUiName(AutomationElement? element)
    {
        if (element is null) return "";
        try { return element.Properties.Name.ValueOrDefault ?? ""; }
        catch { return ""; }
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
            catch (Exception ex) when (ex.GetType().Name.Contains("PropertyNotSupported", StringComparison.Ordinal))
            {
                return false;
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
            Process process;
            try { process = Process.GetProcessById(_app.ProcessId); }
            catch (ArgumentException) { return false; }
            if (process.HasExited) return false;

            // EHP closes and Anmeldung opens as a new window. The previous UIA
            // handle goes stale — that is not a crashed session.
            try
            {
                _mainWindow = FindMainWindow() ?? FirstTopLevelWindow();
                if (_mainWindow is not null)
                    _ = _mainWindow.Title;
            }
            catch
            {
                try { _mainWindow = FirstTopLevelWindow(); }
                catch { _mainWindow = null; }
            }

            return !process.HasExited;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private void EnsureInspectedSelectors()
    {
        if (!_options.RequireInspectedSelectors || IsReplica || UseCs2Workflow)
            return;

        var missing = WilkenSelectorCatalog.MissingRequired(_options.Selectors);
        if (missing.Count == 0)
            return;

        throw new WilkenAutomationException("INSPECT_REQUIRED",
            "Real Wilken UI (WinForms, WPF, Win32, or Java) must be inspected before automation. " +
            "Inside the Citrix Test Environment desktop run: " +
            $"dotnet run --project WilkenAutomation.Worker -- --inspect \"{_options.MainWindowTitle}\". " +
            "Map the dumped AutomationId/Name/ClassName values into Wilken:Selectors. Missing: " +
            string.Join(", ", missing) + ". " + WilkenSessionPolicy.AttachInstructions);
    }

    private bool TryAttachToOpenSession()
    {
        _automation ??= new UIA3Automation();

        var processName = EffectiveProcessName();
        if (!string.IsNullOrWhiteSpace(processName))
        {
            foreach (var process in GetWilkenProcesses(processName))
            {
                if (WilkenSessionPolicy.IsRemoteDisplayProcess(process.ProcessName))
                    continue;
                if (TryAttachToProcess(process))
                    return true;
            }
        }

        return TryAttachByWindowTitle();
    }

    private bool TryAttachToProcess(Process process)
    {
        try
        {
            if (WilkenSessionPolicy.IsRemoteDisplayProcess(process.ProcessName))
            {
                _logger.LogWarning(
                    "Ignoring window on remote-display process {Process}. The worker must run inside the Citrix desktop, not beside the browser.",
                    process.ProcessName);
                return false;
            }

            _app = FlaUI.Core.Application.Attach(process);
            _mainWindow = FindMainWindow() ?? FirstTopLevelWindow();
            return _mainWindow is not null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not attach to PID {Pid}.", process.Id);
            _app = null;
            _mainWindow = null;
            return false;
        }
    }

    private bool TryAttachByWindowTitle()
    {
        if (_automation is null || string.IsNullOrWhiteSpace(_options.MainWindowTitle))
            return false;

        try
        {
            foreach (var child in _automation.GetDesktop().FindAllChildren())
            {
                var title = child.Properties.Name.ValueOrDefault ?? "";
                if (!title.Contains(_options.MainWindowTitle, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pid = child.Properties.ProcessId.ValueOrDefault;
                if (pid <= 0) continue;

                Process process;
                try { process = Process.GetProcessById(pid); }
                catch { continue; }

                if (TryAttachToProcess(process))
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Desktop window scan failed.");
        }

        return false;
    }

    private void AttachToProcess(Process process)
    {
        if (!TryAttachToProcess(process))
        {
            _app = FlaUI.Core.Application.Attach(process);
            _logger.LogInformation("Attached to running Wilken process {Pid}.", process.Id);
        }
    }

    private Window? FirstTopLevelWindow()
    {
        if (_app is null || _automation is null) return null;
        try { return _app.GetAllTopLevelWindows(_automation).FirstOrDefault(); }
        catch { return null; }
    }

    private void LogAttachedWindow()
    {
        if (_mainWindow is null || _app is null) return;
        string framework;
        string className;
        try
        {
            framework = _mainWindow.Properties.FrameworkId.ValueOrDefault ?? "";
            className = _mainWindow.Properties.ClassName.ValueOrDefault ?? "";
        }
        catch
        {
            framework = "";
            className = "";
        }

        _logger.LogInformation(
            "Attached to Wilken pid {Pid}, title '{Title}', framework '{Framework}', class '{Class}'.",
            _app.ProcessId, _mainWindow.Title, framework, className);

        if (WilkenSessionPolicy.IsLikelyJavaWindow(className, framework))
        {
            _logger.LogWarning(
                "Window looks like Java. If --inspect shows almost no controls, enable Java Access Bridge " +
                "(jabswitch -enable) inside the Citrix desktop and inspect again.");
        }
    }

    private Window? FindMainWindow()
    {
        if (_app is null || _automation is null) return null;
        try
        {
            var matches = _app.GetAllTopLevelWindows(_automation)
                .Where(w =>
                {
                    var title = w.Title ?? "";
                    return title.Contains(_options.MainWindowTitle, StringComparison.OrdinalIgnoreCase)
                        || title.Contains("Finanzmanagement", StringComparison.OrdinalIgnoreCase)
                        || title.Contains("EHP 2", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            return matches
                .OrderBy(w => IsStartupOrLoginTitle(w.Title) ? 1 : 0)
                .FirstOrDefault()
                ?? matches.FirstOrDefault();
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

    private void KillProcessesByName(string processName)
    {
        foreach (var process in GetWilkenProcesses(processName))
        {
            try
            {
                _logger.LogInformation("Stopping existing {Name} pid {Pid} so login screens can be shown.", processName, process.Id);
                process.Kill(entireProcessTree: true);
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private void LaunchWilken()
    {
        var exe = EffectiveExecutablePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            throw new WilkenAutomationException("WILKEN_EXE_NOT_FOUND",
                $"Wilken executable not configured or missing: '{exe}'. Set the path on New Run or Wilken:ExecutablePath.",
                sessionLost: true);

        // UseShellExecute=false so Arguments and WILKEN_REPLICA_MANUAL_LOGIN reach the replica.
        // WPF StartupEventArgs.Args is often empty when another process launches the exe.
        var start = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = _options.StartupArguments ?? "",
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            UseShellExecute = false,
            WindowStyle = IsReplica ? ProcessWindowStyle.Normal : ProcessWindowStyle.Minimized
        };
        if (RequiresManualLoginScreens)
            start.Environment["WILKEN_REPLICA_MANUAL_LOGIN"] = "1";

        var process = Process.Start(start)
            ?? throw new WilkenAutomationException("WILKEN_EXE_NOT_FOUND",
                $"Failed to start '{exe}'.", sessionLost: true);
        _app = FlaUI.Core.Application.Attach(process);
        _logger.LogInformation(
            "Launched {Kind} ({Path}) arguments='{Args}' manualLogin={Manual}. {Hint}",
            IsReplica ? "replica" : "Wilken CS/2",
            exe,
            start.Arguments,
            RequiresManualLoginScreens,
            IsReplica
                ? "Window stays visible; automation uses UIA only (no mouse)."
                : "Starts minimized; restore from the taskbar to watch. Automation uses UIA only (no mouse).");
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
                : $"No selector configured for '{selectorKey}'. Real Wilken UI must be inspected first " +
                  $"(dotnet run --project WilkenAutomation.Worker -- --inspect \"{_options.MainWindowTitle}\") " +
                  $"then map Wilken:Selectors:{selectorKey} from AutomationId/Name/ClassName.");

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
                "namecontains" => root.FindAllDescendants()
                    .FirstOrDefault(e => (e.Name ?? "").Contains(value, StringComparison.OrdinalIgnoreCase)),
                "helptextcontains" => root.FindAllDescendants()
                    .FirstOrDefault(e =>
                    {
                        try
                        {
                            return (e.Properties.HelpText.ValueOrDefault ?? "")
                                .Contains(value, StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                    }),
                "frameworkid" => root.FindAllDescendants()
                    .FirstOrDefault(e => (e.Properties.FrameworkId.ValueOrDefault ?? "")
                        .Equals(value, StringComparison.OrdinalIgnoreCase)),
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
