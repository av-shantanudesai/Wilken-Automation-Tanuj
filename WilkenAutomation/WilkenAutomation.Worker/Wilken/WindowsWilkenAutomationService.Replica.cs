using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// DesktopTest path against WilkenCs2ReplicaMock, following AUTOMATION_WORKFLOW.md:
/// navigate → configure → execute → confirm → wait Fortschritt → Liste anzeigen →
/// Druckauswahl → exact PRT spool row → Export Erweitert → force XLSX → file.
/// </summary>
public partial class WindowsWilkenAutomationService
{
    private ExportDefinition ReplicaDefinition =>
        _catalog.ResolveForJob(_job ?? new ExportJob { Department = "Handelsrecht" });

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
            ScreenAnchor.ById("Spool_SelectCurrentPrt"),
            ScreenAnchor.ByText("Druckauswahl")
        },
        MinMatches = 2
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

    private string ReadTitleAndStatusText()
    {
        var parts = new List<string>(2);
        var title = TryFindByAutomationId("Screen_Title");
        if (title is not null) parts.Add(title.Name ?? ReadValue(title));
        var status = TryFindByAutomationId("Status_Text");
        if (status is not null) parts.Add(status.Name ?? ReadValue(status));
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Run one automation step with explicit Expected State / Timeout / OnFailure,
    /// using the session-aware wait (dialog handling, session-loss detection).
    /// </summary>
    private async Task RunReplicaStepAsync(AutomationStep step, CancellationToken ct)
    {
        step.Action();
        try
        {
            await WaitUntilUiAsync(step.ExpectedState, step.Timeout,
                $"expected state after step '{step.Name}'", ct);
        }
        catch (WaitTimeoutException ex)
        {
            if (step.OnFailure is not null)
            {
                try { await step.OnFailure(ct); }
                catch { /* diagnostics must not mask the original failure */ }
            }
            throw new WilkenAutomationException(
                step.FailureErrorCode ?? "STEP_EXPECTED_STATE_TIMEOUT",
                $"Step '{step.Name}' did not reach its expected state within {step.Timeout.TotalSeconds:0}s.",
                inner: ex);
        }
    }

    private async Task ReplicaOpenReportAsync(string? definitionName, CancellationToken ct)
    {
        GuardHealthy();
        var definition = _catalog.TryGet(definitionName) ?? ReplicaDefinition;
        if (string.Equals(definition.Type, ExecutorTypes.View, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(definition.ReplicaNavId))
        {
            throw new WilkenAutomationException("VIEW_NOT_MAPPED",
                $"Export '{definition.Name}' is a VIEW recipe. Run it in Mock mode until real Wilken view controls are inspected.");
        }
        var navId = definition.ReplicaNavId ?? (definition.Name == "Anlagenspiegel" ? "Nav_Anlagenspiegel" : "Nav_Zugangsliste");
        var expected = definition.ReplicaTitleContains ?? definition.DisplayName;

        InvokeControl(FindByAutomationId(navId));
        await WaitUntilUiAsync(
            () => ScreenTitleContains(expected),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"report screen '{expected}'", ct);
    }

    private async Task ReplicaSetPeriodAsync(int fiscalYear, CancellationToken ct)
    {
        GuardHealthy();
        if (!string.Equals(ReplicaDefinition.Name, "Anlagenspiegel", StringComparison.OrdinalIgnoreCase))
        {
            await SetIdValueAsync("Zugang_DateFrom", $"01.01.{fiscalYear}", ct);
            await SetIdValueAsync("Zugang_DateTo", $"31.12.{fiscalYear}", ct);
            await SetIdValueAsync("Zugang_Period_0", "01", ct);
            await SetIdValueAsync("Zugang_Period_1", fiscalYear.ToString(), ct);
            await SetIdValueAsync("Zugang_Period_2", "12", ct);
            await SetIdValueAsync("Zugang_Period_3", fiscalYear.ToString(), ct);
        }
        else
        {
            await SetIdValueAsync("Anlage_Period_0", "01", ct);
            await SetIdValueAsync("Anlage_Period_1", fiscalYear.ToString(), ct);
            await SetIdValueAsync("Anlage_Period_2", "12", ct);
            await SetIdValueAsync("Anlage_Period_3", fiscalYear.ToString(), ct);
        }
    }

    private Task ReplicaSetFachbereichAsync(string department, CancellationToken ct)
    {
        GuardHealthy();
        var id = string.Equals(ReplicaDefinition.Name, "Anlagenspiegel", StringComparison.OrdinalIgnoreCase)
            ? "Anlage_Fachbereich" : "Zugang_Fachbereich";
        return SetSelectorOrIdAsync(id, department, ct);
    }

    private async Task ReplicaExecuteAsync(CancellationToken ct)
    {
        GuardHealthy();
        SessionStatus = WilkenSessionStatus.Busy;
        _runStartedAtUtc = DateTime.UtcNow;
        InvokeControl(FindByAutomationId("Toolbar_Execute"));
        await WaitUntilUiAsync(
            () => TryFindByAutomationId("Confirm_Yes") is not null,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            "confirmation dialog", ct);
        InvokeControl(FindByAutomationId("Confirm_Yes"));
    }

    private async Task ReplicaWaitForProgressAsync(CancellationToken ct)
    {
        await WaitUntilUiAsync(
            () => TryFindByAutomationId("ProgressDialog") is not null,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            "Fortschritt dialog", ct);

        await WaitUntilUiAsync(
            () => TryFindByAutomationId("ProgressDialog") is null,
            TimeSpan.FromMinutes(_options.ReportTimeoutMinutes),
            "Fortschritt dialog to close", ct);

        SessionStatus = WilkenSessionStatus.Ready;
    }

    private async Task ReplicaOpenSpoolAsync(CancellationToken ct)
    {
        GuardHealthy();
        InvokeControl(FindByAutomationId("Nav_ListeAnzeigen"));
        await WaitUntilUiAsync(
            () => TryFindByAutomationId("PrintSelection_Start") is not null,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            "Druckauswahl", ct);
        InvokeControl(FindByAutomationId("PrintSelection_Start"));
        await WaitUntilUiAsync(
            () => ScreenEngine.Matches(SpoolListScreen),
            TimeSpan.FromSeconds(Math.Max(_options.NavigationTimeoutSeconds, 60)),
            $"screen '{SpoolListScreen.Name}' ({SpoolListScreen.MinMatches}+ anchors)", ct);
        await ReplicaSelectGeneratedPrtRowAsync(ct);
    }

    private async Task ReplicaSelectGeneratedPrtRowAsync(CancellationToken ct)
    {
        var selectCurrent = TryFindByAutomationId("Spool_SelectCurrentPrt");
        if (selectCurrent is not null)
            InvokeControl(selectCurrent);

        var match = ReplicaDefinition.SpoolMatch;
        var listName = match?.ListName ?? "B024";
        var protocol = match?.Protocol ?? "Protokoll: Zugangsliste";
        AutomationElement? target = null;

        await WaitUntilUiAsync(() =>
        {
            if (ScreenStatusContains("Ausgewählt") && ScreenStatusContains(listName))
                return true;
            if (TryFindByAutomationId("Spool_CurrentPrt") is not null)
                return true;
            target = FindNewestMatchingPrtRow(listName, protocol);
            return target is not null;
        },
        TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
        $"spool PRT row {listName} after job start", ct);

        if (target is null)
            target = TryFindByAutomationId("Spool_CurrentPrt");
        if (target is null)
            return;

        if (_job is not null)
        {
            var rowText = ReadSubtree(target);
            var created = ParseSpoolTimestamp(rowText);
            _job.SpoolId = created == DateTime.MinValue
                ? $"{listName}-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : $"{listName}-{created:yyyyMMddHHmmss}";
            _logger.LogInformation("Matched spool {SpoolId} for job {JobId}.", _job.SpoolId, _job.JobId);
        }

        WithoutStealingInput(() =>
        {
            try
            {
                if (target.Patterns.SelectionItem.IsSupported)
                {
                    target.Patterns.SelectionItem.Pattern.Select();
                    return;
                }
            }
            catch { }

            foreach (var child in target.FindAllDescendants())
            {
                try
                {
                    if (child.Patterns.SelectionItem.IsSupported)
                    {
                        child.Patterns.SelectionItem.Pattern.Select();
                        return;
                    }
                }
                catch { }
            }
        });
    }

    private AutomationElement? FindNewestMatchingPrtRow(string listName, string protocolText)
    {
        var grid = TryFindByAutomationId("Spool_Grid");
        if (grid is null) return null;

        AutomationElement[] rows;
        try
        {
            rows = grid.FindAllDescendants(cf => cf.ByClassName("DataGridRow"));
            if (rows.Length == 0)
                rows = grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
        }
        catch
        {
            return null;
        }

        AutomationElement? best = null;
        var bestTime = DateTime.MinValue;
        for (var i = 0; i < rows.Length; i++)
        {
            var text = ReadSubtree(rows[i]);
            if (!text.Contains(listName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!text.Contains("PRT", StringComparison.OrdinalIgnoreCase)) continue;
            if (!text.Contains("BHL", StringComparison.OrdinalIgnoreCase)) continue;

            var nextText = i + 1 < rows.Length ? ReadSubtree(rows[i + 1]) : "";
            if (!nextText.Contains(protocolText, StringComparison.OrdinalIgnoreCase))
                continue;

            var created = ParseSpoolTimestamp(text);
            if (created < _runStartedAtUtc.AddMinutes(-2))
                continue;
            var key = SpoolRowKey(text, nextText);
            if (_spoolSnapshot.Contains(key))
                continue;
            if (created >= bestTime)
            {
                bestTime = created;
                best = rows[i];
            }
        }

        return best;
    }

    private void ReplicaCaptureSpoolSnapshot()
    {
        _spoolSnapshot.Clear();
        var grid = TryFindByAutomationId("Spool_Grid");
        if (grid is null) return;
        try
        {
            var rows = grid.FindAllDescendants(cf => cf.ByClassName("DataGridRow"));
            if (rows.Length == 0)
                rows = grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
            for (var i = 0; i < rows.Length; i++)
            {
                var text = ReadSubtree(rows[i]);
                var next = i + 1 < rows.Length ? ReadSubtree(rows[i + 1]) : "";
                _spoolSnapshot.Add(SpoolRowKey(text, next));
            }
        }
        catch { }
    }

    private static string SpoolRowKey(string rowText, string nextText)
    {
        var created = ParseSpoolTimestamp(rowText);
        return $"{created:O}|{rowText}|{nextText}";
    }

    private static DateTime ParseSpoolTimestamp(string rowText)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            rowText, @"(\d{2}\.\d{2}\.\d{4}).{0,8}(\d{2}:\d{2}:\d{2})");
        if (!match.Success) return DateTime.MinValue;
        return DateTime.TryParseExact(
            $"{match.Groups[1].Value} {match.Groups[2].Value}",
            "dd.MM.yyyy HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var value)
            ? value.ToUniversalTime()
            : DateTime.MinValue;
    }

    private async Task<string> ReplicaExportAsync(ExportJob job, CancellationToken ct)
    {
        GuardHealthy();
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        var before = Directory.GetFiles(downloads, "CTLP12*.xlsx")
            .Select(f => (Path: f, Time: File.GetLastWriteTimeUtc(f)))
            .ToList();

        await RunReplicaStepAsync(new AutomationStep
        {
            Name = "Export → Erweitert (open Gitterbox-Export)",
            Action = () => InvokeControl(FindByAutomationId("Spool_OpenAdvancedExport")),
            ExpectedState = () => ScreenEngine.Matches(GitterboxExportScreen),
            Timeout = TimeSpan.FromSeconds(Math.Max(_options.NavigationTimeoutSeconds, 45)),
            FailureErrorCode = "EXPORT_SCREEN_NOT_REACHED",
            OnFailure = _ =>
            {
                var detected = ScreenEngine.Detect(new[] { SpoolListScreen, GitterboxExportScreen });
                _logger.LogWarning(
                    "Gitterbox-Export screen not reached. Currently detected screen: {Screen} (confidence {Confidence:P0}).",
                    detected.Screen?.Name ?? "<unknown>", detected.Confidence);
                return Task.CompletedTask;
            }
        }, ct);

        var formatId = ReplicaFormatRadioId();
        SelectRadio(formatId);
        SelectRadio("Export_Target_Excel");
        SelectRadio("Export_Records_All");
        await WaitUntilUiAsync(
            () => RadioIsSelected(formatId)
                  && RadioIsSelected("Export_Target_Excel")
                  && RadioIsSelected("Export_Records_All"),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{formatId} + Excel + Alle selected", ct);

        InvokeControl(FindByAutomationId("Toolbar_Execute"));
        if (!await TryWaitForExportStartAsync(TimeSpan.FromSeconds(6), ct))
        {
            var run = TryFindByAutomationId("Export_Run") ?? TryFindByAutomationId("Toolbar_Execute");
            if (run is not null)
                InvokeControl(run);
        }

        string? produced = null;
        await WaitUntilUiAsync(() =>
        {
            produced = Directory.GetFiles(downloads, "CTLP12*.xlsx")
                .Select(f => new FileInfo(f))
                .Where(f => f.Length > 0
                            && (before.All(b => b.Path != f.FullName)
                                || File.GetLastWriteTimeUtc(f.FullName) > _runStartedAtUtc))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
            return produced is not null || ScreenStatusContains("Export abgeschlossen");
        },
        TimeSpan.FromMinutes(_options.ExportTimeoutMinutes),
        "downloaded CTLP12.xlsx", ct);

        if (produced is null)
        {
            produced = Directory.GetFiles(downloads, "CTLP12*.xlsx")
                .Select(f => new FileInfo(f))
                .Where(f => f.Length > 0 && f.LastWriteTimeUtc > _runStartedAtUtc.AddMinutes(-2))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        if (produced is null)
            throw new WilkenAutomationException("DOWNLOAD_TIMEOUT",
                "Gitterbox export finished in the UI but no CTLP12.xlsx was found in Downloads.");

        // File appears -> size stops changing -> file is unlocked -> only then validate.
        _lastObservedExportSize = -1;
        await WaitUntilUiAsync(
            () => FileIsStableAndUnlocked(produced!),
            TimeSpan.FromMinutes(Math.Max(1, _options.ExportTimeoutMinutes)),
            "export file size stable and unlocked", ct);

        _logger.LogInformation("Replica export produced {Path} for {JobId}.", produced, job.JobId);
        return produced!;
    }

    private long _lastObservedExportSize = -1;

    /// <summary>
    /// True only when the file has kept the same non-zero size across two polls
    /// and can be opened exclusively (writer has released it).
    /// </summary>
    private bool FileIsStableAndUnlocked(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
            {
                _lastObservedExportSize = -1;
                return false;
            }
            if (info.Length != _lastObservedExportSize)
            {
                _lastObservedExportSize = info.Length;
                return false;
            }
            using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task SetIdValueAsync(string automationId, string value, CancellationToken ct)
    {
        var field = FindByAutomationId(automationId);
        SetControlValue(field, value);
        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFindByAutomationId(automationId), value),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{automationId} = '{value}'", ct);
    }

    private async Task SetSelectorOrIdAsync(string automationId, string value, CancellationToken ct)
    {
        var field = FindByAutomationId(automationId);
        if (field.ControlType == ControlType.ComboBox)
        {
            WithoutStealingInput(() =>
            {
                if (!TrySetComboText(field, value))
                    SetControlValue(field, value);
            });
        }
        else
        {
            SetControlValue(field, value);
        }

        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFindByAutomationId(automationId), value),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{automationId} = '{value}'", ct);
    }

    private string ReplicaFormatRadioId()
    {
        var ext = (_exportSettings.FileExtension ?? ".xlsx").Trim().TrimStart('.').ToUpperInvariant();
        return ext switch
        {
            "CSV" => "Export_Format_CSV",
            "XML" => "Export_Format_XML",
            "HTML" => "Export_Format_HTML",
            "XLS" => "Export_Format_XLS",
            _ => "Export_Format_XLSX"
        };
    }

    private async Task<bool> TryWaitForExportStartAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await WaitUntilUiAsync(
                () => ScreenStatusContains("exportiert") || ScreenStatusContains("Export abgeschlossen"),
                timeout,
                "export start", ct);
            return true;
        }
        catch (WaitTimeoutException)
        {
            return false;
        }
    }

    private bool ScreenStatusContains(string text)
    {
        try
        {
            var status = TryFindByAutomationId("Status_Text");
            var value = status is null ? "" : (status.Name ?? ReadValue(status));
            return value.Contains(text, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SelectRadio(string automationId)
    {
        var radio = FindByAutomationId(automationId);
        WithoutStealingInput(() =>
        {
            try
            {
                if (radio.Patterns.SelectionItem.IsSupported)
                {
                    radio.Patterns.SelectionItem.Pattern.Select();
                    return;
                }
            }
            catch { }

            try
            {
                radio.AsRadioButton().IsChecked = true;
                return;
            }
            catch { }

            InvokeControl(radio);
        });
    }

    private bool RadioIsSelected(string automationId)
    {
        var radio = TryFindByAutomationId(automationId);
        if (radio is null) return false;
        try
        {
            if (radio.Patterns.SelectionItem.IsSupported)
                return radio.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
        }
        catch { }

        try
        {
            if (radio.Patterns.Toggle.IsSupported)
                return radio.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault == ToggleState.On;
        }
        catch { }

        try { return radio.AsRadioButton().IsChecked; }
        catch { return false; }
    }

    private bool ScreenTitleContains(string text)
    {
        try
        {
            var title = TryFindByAutomationId("Screen_Title");
            if (title is not null)
            {
                var value = title.Name ?? ReadValue(title);
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

    private AutomationElement FindByAutomationId(string automationId) =>
        TryFindByAutomationId(automationId)
        ?? throw new WilkenAutomationException("CONTROL_NOT_FOUND",
            $"Replica control '{automationId}' was not found.");

    private AutomationElement? TryFindByAutomationId(string automationId)
    {
        foreach (var root in SearchRoots())
        {
            try
            {
                var hit = root.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                    ?? (root.AutomationId == automationId ? root : null);
                if (hit is not null) return hit;
            }
            catch { }
        }
        return null;
    }

    private static string ReadSubtree(AutomationElement? element)
    {
        if (element is null) return "";
        var parts = new List<string>();
        void Walk(AutomationElement node, int depth)
        {
            if (depth > 6) return;
            try
            {
                var name = node.Name;
                if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
                var value = ReadValue(node);
                if (!string.IsNullOrWhiteSpace(value)) parts.Add(value);
                foreach (var child in node.FindAllChildren())
                    Walk(child, depth + 1);
            }
            catch { }
        }
        Walk(element, 0);
        return string.Join(" ", parts);
    }
}
