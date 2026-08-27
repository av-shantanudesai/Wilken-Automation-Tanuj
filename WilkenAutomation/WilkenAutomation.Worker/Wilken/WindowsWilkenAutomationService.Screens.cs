using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// Screen-state layer: screens are detected from multiple anchors (AutomationIds, titles, status text), never a single signal.
/// </summary>
public partial class WindowsWilkenAutomationService
{
    // ---- Screen-state layer: screens are detected from multiple anchors, never one signal ----

    private ScreenStateEngine? _screenEngineField;

    private ScreenStateEngine ScreenEngine => _screenEngineField ??= new ScreenStateEngine(new ScreenProbe
    {
        ElementExists = id => TryFindByAutomationId(id) is not null,
        ReadScreenText = ReadTitleAndStatusText
    }, TimeSpan.FromMilliseconds(_options.PollingIntervalMs));

    private static readonly ScreenDefinition SpoolListScreen = new()
    {
        Name = "SPOOL_LIST",
        Anchors =
        {
            ScreenAnchor.ById("Spool_Grid"),
            ScreenAnchor.ById("Spool_SelectAll"),
            ScreenAnchor.ByText("Liste anzeigen")
        },
        MinMatches = 1
    };

    private static readonly ScreenDefinition GitterboxExportScreen = new()
    {
        Name = "GITTERBOX_EXPORT",
        Anchors =
        {
            ScreenAnchor.ByText("Gitterbox-Export"),
            ScreenAnchor.ById("Export_Target_Excel"),
            ScreenAnchor.ById("Export_Records_All"),
            ScreenAnchor.ById("Toolbar_Execute")
        },
        MinMatches = 3
    };

    private static readonly ScreenDefinition ProcessManagerScreen = new()
    {
        Name = "PROCESS_MANAGER",
        Anchors =
        {
            ScreenAnchor.ById("ProcessManager_Grid"),
            ScreenAnchor.ById("ProcessManager_OpenSelected"),
            ScreenAnchor.ByText("Prozesse verwalten")
        },
        MinMatches = 2
    };

    private bool IsZugangDefinition =>
        string.Equals(ReplicaDefinition.Name, "Zugangsliste", StringComparison.OrdinalIgnoreCase);

    private string ReadTitleAndStatusText()
    {
        var parts = new List<string>(8);
        try
        {
            foreach (var root in SearchRoots())
            {
                try
                {
                    var name = SafeUiName(root);
                    if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
                    if (root is Window window && !string.IsNullOrWhiteSpace(window.Title))
                        parts.Add(window.Title);
                }
                catch { }
            }
        }
        catch { }

        var title = TryFindByAutomationIdExact("Screen_Title", enterGridRows: false);
        if (title is not null) parts.Add(SafeUiName(title) + " " + ReadValue(title));
        var status = TryFindByAutomationIdExact("Status_Text", enterGridRows: false);
        if (status is not null) parts.Add(SafeUiName(status) + " " + ReadValue(status));
        return string.Join(" ", parts);
    }

    private bool ScreenStatusContains(string text)
    {
        try
        {
            var status = TryFindByAutomationIdExact("Status_Text", enterGridRows: false);
            var value = status is null ? "" : (SafeUiName(status) + " " + ReadValue(status));
            if (value.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
            return ReadTitleAndStatusText().Contains(text, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool ScreenTitleContains(string text)
    {
        try
        {
            var title = TryFindByAutomationId("Screen_Title");
            if (title is not null)
            {
                var value = SafeUiName(title) + " " + ReadValue(title);
                if (value.Contains(text, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            _mainWindow = FindMainWindow() ?? _mainWindow;
            return ReadSubtree(_mainWindow).Contains(text, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exact replica/UIA ids only. Do not use TryFindByAutomationId here: that
    /// falls back to "any grid near the nav text Prozesse verwalten" and can
    /// treat Spool_Grid as the process manager (CAD18 / missing ProcessRow).
    /// </summary>
    private bool IsProcessManagerVisible() =>
        TryFindByAutomationIdExact("ProcessManager_Grid", enterGridRows: false) is not null
        && TryFindByAutomationIdExact("ProcessManager_OpenSelected", enterGridRows: false) is not null;

    /// <summary>
    /// True only when the home work area is showing. Nav_Home is always in the
    /// left tree and must not count as the home screen (that caused CAD18).
    /// </summary>
    private bool IsHomeVisible() =>
        TryFindByAutomationIdExact("Home_Workspace", enterGridRows: false) is not null;

    private bool IsFunctionLockedVisible() =>
        TryFindByAutomationId("FunctionLockedDialog") is not null
        || TryFindByAutomationId("FunctionLocked_OK") is not null
        || ScreenTitleContains("Funktion gesperrt");

    private void DismissFunctionLockedIfPresent()
    {
        var ok = TryFindByAutomationId("FunctionLocked_OK");
        if (ok is not null)
        {
            _logger.LogWarning("Dismissing Funktion gesperrt (CAD18) dialog.");
            InvokeControl(ok);
        }
    }

    private string DetectReplicaScreenName() => DetectActiveScreen();

    /// <summary>
    /// Cheap screen id from exact AutomationIds only. Name/grid fallbacks are too
    /// slow and can confuse spool with Prozesse verwalten.
    /// </summary>
    private string DetectActiveScreen()
    {
        if (IsProcessManagerVisible()) return "PROCESS_MANAGER";
        if (HasExactId("Export_Target_Excel") && HasExactId("Export_Records_All"))
            return "GITTERBOX_EXPORT";
        if (HasExactId("Spool_Grid")) return "SPOOL_LIST";
        if (HasExactId("Zugang_Prozess") || HasExactId("Anlage_Prozess")) return "REPORT";
        if (IsHomeVisible()) return "HOME";
        return "UNKNOWN";
    }

    private bool HasExactId(string automationId) =>
        TryFindByAutomationIdExact(automationId, enterGridRows: false) is not null;
}
