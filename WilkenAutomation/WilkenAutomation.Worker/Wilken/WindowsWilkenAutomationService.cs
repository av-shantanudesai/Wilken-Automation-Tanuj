using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
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
    private readonly ExportSettings _exportSettings;
    private readonly ILogger<WindowsWilkenAutomationService> _logger;

    private UIA3Automation? _automation;
    private FlaUI.Core.Application? _app;
    private Window? _mainWindow;

    public WilkenSessionStatus SessionStatus { get; private set; } = WilkenSessionStatus.NotRunning;

    public WindowsWilkenAutomationService(
        WilkenOptions options,
        IWilkenCredentialProvider credentials,
        ExportSettings exportSettings,
        ILogger<WindowsWilkenAutomationService> logger)
    {
        _options = options;
        _credentials = credentials;
        _exportSettings = exportSettings;
        _logger = logger;
    }

    public Task BeginJobAsync(ExportJob job, RunConfig config, CancellationToken ct) => Task.CompletedTask;

    public async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (TryIsAlive() && SessionStatus == WilkenSessionStatus.Ready)
            return;

        SessionStatus = WilkenSessionStatus.Starting;
        _automation ??= new UIA3Automation();

        var processName = EffectiveProcessName();
        var existing = GetWilkenProcesses(processName);
        if (existing.Length > 0)
        {
            _app = FlaUI.Core.Application.Attach(existing[0]);
            _logger.LogInformation("Attached to running Wilken process {Pid}.", existing[0].Id);
            if (FindMainWindow() is null)
            {
                _logger.LogWarning("Wilken process {Pid} has no main window (user closed it). Restarting.", existing[0].Id);
                await KillTrackedProcessAsync();
                LaunchWilken();
            }
        }
        else
        {
            LaunchWilken();
        }

        await WaitHelper.WaitUntilAsync(() =>
        {
            _mainWindow = FindMainWindow();
            return _mainWindow is not null;
        },
        TimeSpan.FromSeconds(_options.StartupTimeoutSeconds),
        TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
        $"Wilken main window '{_options.MainWindowTitle}'", ct);

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

        userBox.AsTextBox().Text = credentials.Username;
        Find("LoginPassword").AsTextBox().Text = credentials.Password;
        InvokeControl(Find("LoginButton"));
        _logger.LogInformation("Login submitted for user (name not logged).");

        await WaitUntilUiAsync(
            () => TryFind("LoginUsername") is null,
            TimeSpan.FromSeconds(_options.LoginTimeoutSeconds),
            "login completion", ct);
    }

    public async Task SelectClientAsync(string client, CancellationToken ct)
    {
        GuardHealthy();
        HandleDialogs();
        await SetSelectorValueAsync("ClientField", client, ct);
    }

    public async Task OpenAssetAccountingAsync(CancellationToken ct)
    {
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
        GuardHealthy();
        HandleDialogs();
        var field = Find("FiscalYearField");
        field.AsTextBox().Text = fiscalYear.ToString();
        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFind("FiscalYearField"), fiscalYear.ToString()),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"fiscal year '{fiscalYear}'", ct);
    }

    public async Task SelectDepartmentAsync(string department, CancellationToken ct)
    {
        GuardHealthy();
        HandleDialogs();
        await SetSelectorValueAsync("DepartmentField", department, ct);
    }

    public Task StartEvaluationAsync(CancellationToken ct)
    {
        GuardHealthy();
        HandleDialogs();
        TryCollapse(TryFind("ClientField"));
        TryCollapse(TryFind("DepartmentField"));
        InvokeControl(Find("ExecuteButton"));
        SessionStatus = WilkenSessionStatus.Busy;
        return Task.CompletedTask;
    }

    public async Task WaitForReportReadyAsync(CancellationToken ct)
    {
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
        GuardHealthy();
        HandleDialogs();
        InvokeControl(Find("SpoolMenu"));
        await WaitUntilUiAsync(() => SpoolHasReport(),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            "spool list with a generated report", ct);
    }

    public async Task<string> ExportAsync(ExportJob job, CancellationToken ct)
    {
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
        nameBox.AsTextBox().Text = tempPath;
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

    // ------------------------------------------------------------- helpers ----

    /// <summary>
    /// WPF ComboBox.Select() expands a popup whose items are not children of the
    /// combo in the UIA tree, so FlaUI leaves the dropdown open and never selects.
    /// Prefer Value/editable text, then click the popup ListItem, then keyboard.
    /// </summary>
    private async Task SetSelectorValueAsync(string selectorKey, string value, CancellationToken ct)
    {
        var field = Find(selectorKey);
        if (field.ControlType == ControlType.ComboBox)
            await SelectComboItemAsync(selectorKey, field, value, ct);
        else
            field.AsTextBox().Text = value;

        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFind(selectorKey), value),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{selectorKey} = '{value}'", ct);
    }

    private async Task SelectComboItemAsync(string selectorKey, AutomationElement field, string value, CancellationToken ct)
    {
        TryCollapse(field);

        // WPF dropdown items live in a popup HWND, not as children of the ComboBox.
        if (await TryClickComboListItemAsync(selectorKey, value, ct))
            return;

        field = Find(selectorKey);
        if (TrySetComboText(field, value) && ControlShowsValue(field, value))
            return;

        TryTypeComboValue(Find(selectorKey), value);
    }

    private static bool TrySetComboText(AutomationElement field, string value)
    {
        try
        {
            var combo = field.AsComboBox();
            if (combo.IsEditable)
            {
                combo.EditableText = value;
                return true;
            }
        }
        catch
        {
            // Not an editable combo.
        }

        try
        {
            if (field.Patterns.Value.IsSupported && !field.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault)
            {
                field.Patterns.Value.Pattern.SetValue(value);
                return true;
            }
        }
        catch
        {
            // Read-only or unsupported Value pattern.
        }

        return false;
    }

    private async Task<bool> TryClickComboListItemAsync(string selectorKey, string value, CancellationToken ct)
    {
        var field = Find(selectorKey);
        try { field.AsComboBox().Expand(); }
        catch { field.Click(); }

        AutomationElement? item = null;
        try
        {
            await WaitHelper.WaitUntilAsync(() =>
            {
                item = FindComboListItem(value);
                return item is not null;
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(Math.Max(100, _options.PollingIntervalMs)),
            $"combo list item '{value}'", ct);
        }
        catch (WaitTimeoutException)
        {
            TryCollapse(TryFind(selectorKey));
            return false;
        }

        try
        {
            if (item!.Patterns.SelectionItem.IsSupported)
                item.Patterns.SelectionItem.Pattern.Select();
            else if (item.Patterns.Invoke.IsSupported)
                item.Patterns.Invoke.Pattern.Invoke();
            else
                item.Click();
        }
        catch
        {
            item!.Click();
        }

        TryCollapse(TryFind(selectorKey));
        return ControlShowsValue(TryFind(selectorKey), value);
    }

    private AutomationElement? FindComboListItem(string value)
    {
        bool Matches(AutomationElement e)
        {
            var name = e.Name ?? "";
            if (name.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                if (e.Patterns.Value.IsSupported)
                {
                    var text = e.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
                    if (text.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }

            try
            {
                var child = e.FindFirstDescendant(cf => cf.ByName(value));
                if (child is not null) return true;
            }
            catch { }

            return false;
        }

        IEnumerable<AutomationElement> ListItems(AutomationElement root)
        {
            try
            {
                return root.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            }
            catch
            {
                return Array.Empty<AutomationElement>();
            }
        }

        if (_mainWindow is not null)
        {
            foreach (var candidate in ListItems(_mainWindow))
                if (Matches(candidate)) return candidate;
        }

        if (_automation is null || _app is null) return null;

        AutomationElement? NamedListItem(AutomationElement root)
        {
            try
            {
                return root.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.ListItem).And(cf.ByName(value)));
            }
            catch
            {
                return null;
            }
        }

        foreach (var window in _app.GetAllTopLevelWindows(_automation))
        {
            var named = NamedListItem(window);
            if (named is not null) return named;
            foreach (var candidate in ListItems(window))
                if (Matches(candidate)) return candidate;
        }

        try
        {
            var desktopHit = NamedListItem(_automation.GetDesktop());
            if (desktopHit is not null && desktopHit.Properties.ProcessId == _app.ProcessId)
                return desktopHit;

            foreach (var list in _automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.List)))
            {
                if (list.Properties.ProcessId != _app.ProcessId) continue;
                foreach (var candidate in ListItems(list))
                    if (Matches(candidate)) return candidate;
            }
        }
        catch { }

        return null;
    }

    private static void TryTypeComboValue(AutomationElement field, string value)
    {
        TryCollapse(field);
        field.Focus();
        Keyboard.Type(value);
        Keyboard.Press(VirtualKeyShort.ENTER);
        TryCollapse(field);
    }

    private static void TryCollapse(AutomationElement? field)
    {
        if (field is null) return;
        try { field.AsComboBox().Collapse(); }
        catch { }
    }

    private static bool ControlShowsValue(AutomationElement? element, string value)
    {
        if (element is null) return false;
        if (ReadValue(element).Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var selected = element.AsComboBox().SelectedItem;
            var text = selected?.Text ?? selected?.Name ?? "";
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }

        try
        {
            var combo = element.AsComboBox();
            if (combo.IsEditable && (combo.EditableText ?? "").Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }

        return false;
    }

    private void InvokeControl(AutomationElement element)
    {
        var invoked = false;
        try
        {
            if (element.Patterns.Invoke.IsSupported)
            {
                element.Patterns.Invoke.Pattern.Invoke();
                invoked = true;
            }
        }
        catch
        {
            // WPF sometimes exposes Invoke but fails to raise the routed event.
        }

        if (element.ControlType == ControlType.MenuItem)
        {
            if (invoked) return;
            element.AsMenuItem().Invoke();
            return;
        }

        try
        {
            element.Click();
        }
        catch when (invoked)
        {
            // Invoke already succeeded.
        }
        catch
        {
            if (!invoked)
                element.AsButton().Invoke();
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

    private string EffectiveProcessName() =>
        string.IsNullOrWhiteSpace(_options.ProcessName)
            ? Path.GetFileNameWithoutExtension(_options.ExecutablePath)
            : _options.ProcessName;

    private static Process[] GetWilkenProcesses(string processName) =>
        string.IsNullOrEmpty(processName) ? Array.Empty<Process>() : Process.GetProcessesByName(processName);

    private void LaunchWilken()
    {
        if (string.IsNullOrEmpty(_options.ExecutablePath) || !File.Exists(_options.ExecutablePath))
            throw new WilkenAutomationException("WILKEN_EXE_NOT_FOUND",
                $"Wilken executable not configured or missing: '{_options.ExecutablePath}'. Set Wilken:ExecutablePath.",
                sessionLost: true);

        _app = FlaUI.Core.Application.Launch(_options.ExecutablePath);
        _logger.LogInformation("Launched Wilken CS/2 ({Path}).", _options.ExecutablePath);
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

    /// <summary>
    /// Dismiss informational popups by clicking a named OK/Close button via UIA.
    /// Known dialogs (save export) are left alone. Unknown dialogs without a
    /// dismiss button abort the attempt so the executor can screenshot and retry.
    /// </summary>
    private void HandleDialogs()
    {
        for (var i = 0; i < 5; i++)
        {
            if (!TryDismissOneDialog()) break;
        }
    }

    private bool TryDismissOneDialog()
    {
        var overlay = TryFind("UnexpectedDialog");
        if (overlay is not null)
        {
            var ok = TryFind("DialogOkButton") ?? FindButtonByName(overlay, DismissButtonNames());
            if (ok is null)
                throw new WilkenAutomationException("UNEXPECTED_DIALOG",
                    "Unexpected overlay dialog has no OK/Close button. Aborting attempt for safe recovery.");
            _logger.LogWarning("Dismissing unexpected overlay dialog.");
            InvokeControl(ok);
            return true;
        }

        if (_mainWindow is null) return false;
        Window[] modals;
        try { modals = _mainWindow.ModalWindows; }
        catch { return false; }

        foreach (var modal in modals)
        {
            var title = modal.Title ?? "<untitled>";
            if (IsKnownDialog(title)) continue;

            var ok = TryFindIn(modal, "DialogOkButton") ?? FindButtonByName(modal, DismissButtonNames());
            if (ok is not null)
            {
                _logger.LogWarning("Dismissing unexpected dialog '{Title}'.", title);
                InvokeControl(ok);
                return true;
            }

            throw new WilkenAutomationException("UNEXPECTED_DIALOG",
                $"Unexpected modal dialog '{title}' has no dismiss button. Aborting attempt for safe recovery.");
        }

        return false;
    }

    private bool IsKnownDialog(string title)
    {
        var known = _options.Selectors.GetValueOrDefault("KnownDialogTitles", "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return known.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private string[] DismissButtonNames() =>
        (_options.Selectors.GetValueOrDefault("DismissibleButtonNames", "OK|Close|Ja|Yes|Weiter")
            ?? "OK|Close|Ja|Yes|Weiter")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static AutomationElement? FindButtonByName(AutomationElement root, string[] names)
    {
        try
        {
            foreach (var name in names)
            {
                var button = root.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.Button).And(cf.ByName(name)));
                if (button is not null) return button;
            }
        }
        catch { }
        return null;
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
