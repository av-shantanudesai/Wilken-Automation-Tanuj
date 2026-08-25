using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// DesktopTest path against WilkenCs2ReplicaMock, following AUTOMATION_WORKFLOW:
/// Prozesse verwalten → select process → execute → wait Fortschritt → Liste anzeigen →
/// Druckauswahl → exact data spool row → Export Erweitert → force XLSX → validate →
/// InternalWindow_Close until ProcessManager_Grid (never re-click Prozesse verwalten).
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
            ScreenAnchor.ById("Spool_SelectAll"),
            ScreenAnchor.ByText("Liste anzeigen")
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
        var parts = new List<string>(2);
        var title = TryFindByAutomationId("Screen_Title");
        if (title is not null) parts.Add(SafeUiName(title) + " " + ReadValue(title));
        var status = TryFindByAutomationId("Status_Text");
        if (status is not null) parts.Add(SafeUiName(status) + " " + ReadValue(status));
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
        if (string.Equals(definition.Type, ExecutorTypes.Spool, StringComparison.OrdinalIgnoreCase))
            await ReplicaOpenSavedProcessAsync(definition, ct);
        else
        {
            var navId = definition.ReplicaNavId
                ?? throw new WilkenAutomationException("VIEW_NOT_MAPPED",
                    $"Export '{definition.Name}' has no replica navigation mapping.");
            await ActivateReplicaControlAsync(navId, ct);
            await WaitUntilUiAsync(
                () => ScreenTitleContains(definition.ReplicaTitleContains ?? definition.DisplayName),
                TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
                $"report screen '{definition.DisplayName}'", ct);
        }

        await ReplicaVerifyStaticFieldsAsync(definition, ct);
    }

    /// <summary>
    /// Screenshots / reviewed workflow: start at Prozesse verwalten when it is not
    /// already open. Never click Nav_ProzesseVerwalten while a child report/list/export
    /// window is still active — that produces Funktion gesperrt (CAD18).
    /// </summary>
    private async Task ReplicaOpenSavedProcessAsync(ExportDefinition definition, CancellationToken ct)
    {
        var (program, number, name) = ResolveSavedProcess(definition);

        if (IsFunctionLockedVisible())
        {
            DismissFunctionLockedIfPresent();
            await ReplicaReturnToProcessManagerAsync(ct);
        }
        else if (!IsProcessManagerVisible())
        {
            await ActivateReplicaControlAsync("Nav_ProzesseVerwalten", ct);
            await WaitUntilUiAsync(
                () => IsProcessManagerVisible() || IsFunctionLockedVisible(),
                TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
                "Prozesse verwalten", ct);

            if (IsFunctionLockedVisible())
            {
                _logger.LogWarning("Funktion gesperrt after opening Prozesse verwalten; unwinding child windows first.");
                DismissFunctionLockedIfPresent();
                await ReplicaReturnToProcessManagerAsync(ct);
            }
        }

        var rowId = $"ProcessRow_{program}_{number}";

        AutomationElement? row = null;
        await WaitUntilUiAsync(() =>
        {
            row = TryFindByAutomationId(rowId) ?? FindProcessRow(program, number, name);
            return row is not null;
        }, TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"process row {program}/{number} '{name}'", ct);

        SelectRow(row!);
        var open = TryFindByAutomationId("ProcessManager_OpenSelected");
        if (open is not null)
            InvokeControl(open);
        else
            throw new WilkenAutomationException("CONTROL_NOT_FOUND",
                "Process manager is missing ProcessManager_OpenSelected; cannot open the saved process without a mouse double-click.");

        var processField = definition.Name.Contains("Zugang", StringComparison.OrdinalIgnoreCase)
            ? "Zugang_Prozess" : "Anlage_Prozess";
        var nameField = definition.Name.Contains("Zugang", StringComparison.OrdinalIgnoreCase)
            ? "Zugang_Bezeichnung" : "Anlage_Bezeichnung";
        await WaitUntilUiAsync(
            () => ScreenTitleContains(definition.ReplicaTitleContains ?? "Anlagenspiegel erstellen")
                  && ControlShowsValue(TryFindByAutomationId(processField), number)
                  && ControlShowsValue(TryFindByAutomationId(nameField), name),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"saved process {number} '{name}' loaded", ct);
    }

    private static (string Program, string Number, string Name) ResolveSavedProcess(ExportDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.ReplicaProcessNumber))
        {
            return (
                definition.ReplicaProcessProgram ?? "CAB015",
                definition.ReplicaProcessNumber,
                definition.ReplicaProcessName ?? definition.DisplayName);
        }

        return definition.Name switch
        {
            "Zugangsliste" => ("CAB024", "001", "Zugangsliste"),
            "Anlagenspiegel" => ("CAB015", "001", "Anlagenspiegel nach Anlagen"),
            _ => ("CAB015", "003", "Alle Anlagen nach Konten verdichtet")
        };
    }

    private AutomationElement? FindProcessRow(string program, string number, string name)
    {
        var grid = TryFindByAutomationId("ProcessManager_Grid");
        if (grid is null) return null;
        try
        {
            var rows = grid.FindAllDescendants(cf => cf.ByClassName("DataGridRow"));
            if (rows.Length == 0)
                rows = grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
            foreach (var row in rows)
            {
                var text = ReadSubtree(row);
                if (text.Contains(program, StringComparison.OrdinalIgnoreCase)
                    && text.Contains(number, StringComparison.OrdinalIgnoreCase)
                    && text.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return row;
            }
        }
        catch { }
        return null;
    }

    private async Task ReplicaVerifyStaticFieldsAsync(ExportDefinition definition, CancellationToken ct)
    {
        if (string.Equals(definition.Name, "Zugangsliste", StringComparison.OrdinalIgnoreCase))
        {
            await VerifyValueAsync("Zugang_Prozess", "001", ct);
            await VerifyValueAsync("Zugang_Bezeichnung", "Zugangsliste", ct);
            await SetCheckAsync("Field_Aktiv", true, ct);
            await SetCheckAsync("Field_AutoDeactivate", false, ct);
            await SetCheckAsync("Field_Laufprotokoll", false, ct);
            await SetSelectorOrIdAsync("Zugang_Art", "Bericht", ct);
            await SetSelectorOrIdAsync("Zugang_Bericht", "NACH ANLAGEN", ct);
            await SetSelectorOrIdAsync("Zugang_Wertart", "Ist", ct);
            await SetSelectorOrIdAsync("Field_ErstellungArt", "Druckversion", ct);
            await SetCheckAsync("Field_SumMainAsset", false, ct);
            await SetCheckAsync("Zugang_Gegenkonto", false, ct);
            await SetCheckAsync("Zugang_UmbuchungBilanzposition", true, ct);
            await SetCheckAsync("Zugang_UmbuchungAnlage", true, ct);
            return;
        }

        var isCondensed = string.Equals(definition.Name, "AlleAnlagenNachKontenVerdichtet", StringComparison.OrdinalIgnoreCase);
        var process = isCondensed ? (definition.ReplicaProcessNumber ?? "003") : "001";
        var bezeichnung = isCondensed ? "Alle Anlagen nach Konten verdichtet" : "Anlagenspiegel nach Anlagen";
        await VerifyValueAsync("Anlage_Prozess", process, ct);
        await VerifyValueAsync("Anlage_Bezeichnung", bezeichnung, ct);
        await SetCheckAsync("Field_Aktiv", true, ct);
        await SetCheckAsync("Field_AutoDeactivate", false, ct);
        await SetCheckAsync("Field_Laufprotokoll", true, ct);
        await SetSelectorOrIdAsync("Anlage_Art", "Kompletter Datenbestand", ct);
        await SetSelectorOrIdAsync("Anlage_Wertart", "Ist", ct);
        await SetSelectorOrIdAsync("Field_ErstellungArt", "Druckversion", ct);
        await SetCheckAsync("Field_SumMainAsset", false, ct);
        await SetCheckAsync("Anlage_Zugaenge", !isCondensed, ct);
        await SetCheckAsync("Anlage_Abgaenge", !isCondensed, ct);
        await SetCheckAsync("Anlage_Umbuchung", !isCondensed, ct);
        await VerifyValueAsync("Field_Status", "OK", ct);
    }

    private async Task ReplicaSetPeriodAsync(int fiscalYear, CancellationToken ct)
    {
        GuardHealthy();
        if (IsZugangDefinition)
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
        var id = IsZugangDefinition ? "Zugang_Fachbereich" : "Anlage_Fachbereich";
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

        // Phase changes (Anlagenselektion / Ermitteln der Werte / Der Anlagenspiegel wird erstellt)
        // are normal. Never click Progress_Cancel. Wait until the dialog closes itself.
        var lastMessage = "";
        await WaitUntilUiAsync(() =>
        {
            var messageEl = TryFindByAutomationId("Progress_Message");
            var message = messageEl is null ? "" : (SafeUiName(messageEl) + " " + ReadValue(messageEl));
            if (!string.IsNullOrWhiteSpace(message) && !string.Equals(message, lastMessage, StringComparison.Ordinal))
            {
                lastMessage = message;
                _logger.LogInformation("Fortschritt: {Message}", message);
            }
            return TryFindByAutomationId("ProgressDialog") is null;
        },
        TimeSpan.FromMinutes(_options.ReportTimeoutMinutes),
        "Fortschritt dialog to close", ct);

        SessionStatus = WilkenSessionStatus.Ready;
    }

    private async Task ReplicaOpenSpoolAsync(CancellationToken ct)
    {
        GuardHealthy();
        await ActivateReplicaControlAsync("Nav_ListeAnzeigen", ct);

        var printOpened = false;
        try
        {
            await WaitUntilUiAsync(
                () => TryFindByAutomationId("PrintSelection_Start") is not null,
                TimeSpan.FromSeconds(8),
                "Druckauswahl", ct);
            printOpened = true;
        }
        catch (WaitTimeoutException)
        {
            // TreeView SelectionItem.Select is a no-op when Liste anzeigen is already
            // selected after the previous job. Invoke the dedicated open control instead.
            _logger.LogWarning("Liste anzeigen was still selected; opening Druckauswahl via Nav_ListeAnzeigen_Open.");
            var open = TryFindByAutomationId("Nav_ListeAnzeigen_Open");
            if (open is not null)
                InvokeControl(open);
            else
                await ActivateReplicaControlAsync("Nav_ListeAnzeigen", ct);
        }

        if (!printOpened)
        {
            await WaitUntilUiAsync(
                () => TryFindByAutomationId("PrintSelection_Start") is not null,
                TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
                "Druckauswahl", ct);
        }

        InvokeControl(FindByAutomationId("PrintSelection_Start"));
        await WaitUntilUiAsync(
            () => ScreenEngine.Matches(SpoolListScreen),
            TimeSpan.FromSeconds(Math.Max(_options.NavigationTimeoutSeconds, 60)),
            $"screen '{SpoolListScreen.Name}' ({SpoolListScreen.MinMatches}+ anchors)", ct);
        await ReplicaSelectGeneratedPrtRowAsync(ct);
    }

    private async Task ReplicaSelectGeneratedPrtRowAsync(CancellationToken ct)
    {
        var match = ReplicaDefinition.SpoolMatch;
        var listName = match?.ListName ?? "5J0102";
        var report = match?.ReportDescription ?? ReplicaDefinition.DisplayName;
        AutomationElement? target = null;
        string? error = null;

        await WaitUntilUiAsync(() =>
        {
            error = null;
            var latest = TryFindByAutomationId("Spool_LatestDataRow");
            if (latest is not null)
            {
                target = latest;
                return true;
            }
            var found = FindMatchingDataSpoolRows(match, out var ambiguous);
            if (ambiguous)
            {
                error = "AMBIGUOUS_SPOOL_MATCH";
                return true;
            }
            target = found;
            return target is not null;
        },
        TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
        $"spool data row '{report}' after job start", ct);

        if (error == "AMBIGUOUS_SPOOL_MATCH")
            throw new WilkenAutomationException("AMBIGUOUS_SPOOL_MATCH",
                $"More than one new spool row matches '{report}' after job start. Refusing to guess.");
        if (target is null)
            throw new WilkenAutomationException("SPOOL_ENTRY_NOT_FOUND",
                $"No new data-report spool row for '{report}' (list {listName}) was found after job start. Protocol rows are ignored.");

        if (_job is not null)
        {
            var rowText = ReadSubtree(target);
            var created = ParseSpoolTimestamp(rowText);
            _job.SpoolId = created == DateTime.MinValue
                ? $"{listName}-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : $"{listName}-{created:yyyyMMddHHmmss}";
            _logger.LogInformation("Matched data spool {SpoolId} for job {JobId}.", _job.SpoolId, _job.JobId);
        }

        SelectRow(target);
    }

    /// <summary>
    /// Selects the CSA metadata row whose next STOP description is the data report.
    /// Protocol rows (Protokoll: …) and excluded report names are rejected.
    /// </summary>
    private AutomationElement? FindMatchingDataSpoolRows(SpoolMatchSpec? match, out bool ambiguous)
    {
        ambiguous = false;
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

        var listName = match?.ListName;
        var extension = match?.Extension;
        var user = match?.User ?? "BHL";
        var report = match?.ReportDescription ?? ReplicaDefinition.DisplayName;
        var exclude = match?.ExcludeDescription;
        var hits = new List<(AutomationElement Row, DateTime Created)>();

        for (var i = 0; i < rows.Length; i++)
        {
            var text = ReadSubtree(rows[i]);
            if (text.Contains("Protokoll:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(listName) && !text.Contains(listName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(extension) && !text.Contains(extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (!text.Contains(user, StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Contains("PRT", StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(extension) || !string.Equals(extension, "PRT", StringComparison.OrdinalIgnoreCase)))
                continue;

            var nextText = i + 1 < rows.Length ? ReadSubtree(rows[i + 1]) : "";
            if (nextText.Contains("Protokoll:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!nextText.Contains(report, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(exclude)
                && nextText.Contains(exclude, StringComparison.OrdinalIgnoreCase)
                && !report.Contains(exclude, StringComparison.OrdinalIgnoreCase))
                continue;

            var created = ParseSpoolTimestamp(text);
            if (created < _runStartedAtUtc.AddMinutes(-2)) continue;
            if (_spoolSnapshot.Contains(SpoolRowKey(text, nextText))) continue;
            hits.Add((rows[i], created));
        }

        if (hits.Count == 0) return null;
        var newest = hits.Max(h => h.Created);
        var top = hits.Where(h => h.Created == newest).ToList();
        if (top.Count > 1)
        {
            ambiguous = true;
            return null;
        }
        return top[0].Row;
    }

    private void SelectRow(AutomationElement target)
    {
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

        await Task.Delay(Math.Max(250, _options.PollingIntervalMs), ct);
        await ReplicaReturnToProcessManagerAsync(ct);
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

    private async Task VerifyValueAsync(string automationId, string expected, CancellationToken ct)
    {
        try
        {
            await WaitUntilUiAsync(
                () => ControlShowsValue(TryFindByAutomationId(automationId), expected),
                TimeSpan.FromSeconds(Math.Min(_options.NavigationTimeoutSeconds, 15)),
                $"{automationId} = '{expected}'", ct);
        }
        catch (WaitTimeoutException ex)
        {
            var actual = TryFindByAutomationId(automationId) is { } el ? (SafeUiName(el) + " " + ReadValue(el)) : "<missing>";
            throw new WilkenAutomationException("FIELD_MISMATCH",
                $"Field '{automationId}' is '{actual}', expected '{expected}'.", inner: ex);
        }
    }

    private async Task SetCheckAsync(string automationId, bool expected, CancellationToken ct)
    {
        var box = TryFindByAutomationId(automationId);
        if (box is null)
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"Replica checkbox '{automationId}' was not found.");

        if (CheckIsOn(box) != expected)
        {
            WithoutStealingInput(() =>
            {
                try
                {
                    if (box.Patterns.Toggle.IsSupported)
                    {
                        box.Patterns.Toggle.Pattern.Toggle();
                        return;
                    }
                }
                catch { }

                try
                {
                    box.AsCheckBox().IsChecked = expected;
                    return;
                }
                catch { }

                InvokeControl(box);
            });
        }

        await WaitUntilUiAsync(
            () => CheckIsOn(TryFindByAutomationId(automationId) ?? box) == expected,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{automationId} {(expected ? "checked" : "unchecked")}", ct);
    }

    private static bool CheckIsOn(AutomationElement? element)
    {
        if (element is null) return false;
        try
        {
            if (element.Patterns.Toggle.IsSupported)
                return element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault == ToggleState.On;
        }
        catch { }

        try { return element.AsCheckBox().IsChecked == true; }
        catch { return false; }
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
            var value = status is null ? "" : (SafeUiName(status) + " " + ReadValue(status));
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

    private AutomationElement FindByAutomationId(string automationId) =>
        TryFindByAutomationId(automationId)
        ?? throw new WilkenAutomationException("CONTROL_NOT_FOUND",
            $"Replica control '{automationId}' was not found.");

    /// <summary>
    /// TreeView items expose SelectionItem, not Invoke. Re-selecting an already
    /// selected node does not fire SelectedItemChanged, so force a toggle via Nav_Home
    /// and wait until the previous selection is actually cleared.
    /// </summary>
    private async Task ActivateReplicaControlAsync(string automationId, CancellationToken ct)
    {
        var element = FindByAutomationId(automationId);
        var alreadySelected = false;
        try
        {
            alreadySelected = element.Patterns.SelectionItem.IsSupported
                && element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
        }
        catch { }

        if (alreadySelected && !string.Equals(automationId, "Nav_Home", StringComparison.OrdinalIgnoreCase))
        {
            var home = TryFindByAutomationId("Nav_Home");
            WithoutStealingInput(() =>
            {
                try
                {
                    if (home?.Patterns.SelectionItem.IsSupported == true)
                        home.Patterns.SelectionItem.Pattern.Select();
                }
                catch { }
            });

            try
            {
                await WaitUntilUiAsync(() =>
                {
                    var current = TryFindByAutomationId(automationId);
                    if (current is null) return true;
                    try
                    {
                        return !current.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
                    }
                    catch
                    {
                        return true;
                    }
                }, TimeSpan.FromSeconds(3), $"deselect {automationId} via Nav_Home", ct);
            }
            catch (WaitTimeoutException)
            {
                _logger.LogWarning("Nav_Home did not deselect {NavId}; will re-select anyway.", automationId);
            }

            element = FindByAutomationId(automationId);
        }

        WithoutStealingInput(() =>
        {
            try
            {
                if (element.Patterns.SelectionItem.IsSupported)
                {
                    element.Patterns.SelectionItem.Pattern.Select();
                    return;
                }
            }
            catch { }

            InvokeControl(element);
        });
    }

    private bool IsProcessManagerVisible() =>
        TryFindByAutomationId("ProcessManager_Grid") is not null;

    private bool IsHomeVisible() => TryFindByAutomationId("Home_Workspace") is not null;

    private bool IsFunctionLockedVisible() =>
        TryFindByAutomationId("FunctionLockedDialog") is not null
        || TryFindByAutomationId("FunctionLocked_OK") is not null;

    private void DismissFunctionLockedIfPresent()
    {
        var ok = TryFindByAutomationId("FunctionLocked_OK");
        if (ok is not null)
        {
            _logger.LogWarning("Dismissing Funktion gesperrt (CAD18) dialog.");
            InvokeControl(ok);
        }
    }

    private string DetectReplicaScreenName()
    {
        if (IsProcessManagerVisible()) return "PROCESS_MANAGER";
        // Prefer live export radios over leftover "Gitterbox-Export" status text.
        if (TryFindByAutomationId("Export_Target_Excel") is not null
            && TryFindByAutomationId("Export_Records_All") is not null)
            return "GITTERBOX_EXPORT";
        if (TryFindByAutomationId("Spool_Grid") is not null) return "SPOOL_LIST";
        if (TryFindByAutomationId("Zugang_Prozess") is not null || TryFindByAutomationId("Anlage_Prozess") is not null)
            return "REPORT";
        if (IsHomeVisible()) return "HOME";
        return "UNKNOWN";
    }

    /// <summary>
    /// After a validated export, close one Wilken child window at a time with
    /// InternalWindow_Close until ProcessManager_Grid is visible. Verify after
    /// every click. Never close the outer application window.
    /// </summary>
    private async Task ReplicaReturnToProcessManagerAsync(CancellationToken ct)
    {
        GuardHealthy();
        DismissFunctionLockedIfPresent();

        if (IsProcessManagerVisible())
        {
            _logger.LogInformation("Already on Prozesse verwalten; skip unwind.");
            return;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(_options.NavigationTimeoutSeconds * 3, 90));
        var deadline = DateTime.UtcNow + timeout;
        var closes = 0;
        const int maxCloses = 8;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfSessionLost();
            DismissFunctionLockedIfPresent();

            if (IsProcessManagerVisible())
            {
                _logger.LogInformation("Returned to Prozesse verwalten after {Closes} internal close(s).", closes);
                return;
            }

            if (IsHomeVisible())
            {
                await ActivateReplicaControlAsync("Nav_ProzesseVerwalten", ct);
                await WaitUntilUiAsync(
                    () => IsProcessManagerVisible() || IsFunctionLockedVisible(),
                    TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
                    "Prozesse verwalten from home", ct);
                if (IsFunctionLockedVisible())
                {
                    DismissFunctionLockedIfPresent();
                    continue;
                }
                return;
            }

            if (closes >= maxCloses)
                break;

            var close = TryFindByAutomationId("InternalWindow_Close");
            if (close is null)
            {
                await Task.Delay(_options.PollingIntervalMs, ct);
                continue;
            }

            var before = DetectReplicaScreenName();
            _logger.LogInformation("InternalWindow_Close from {Screen}.", before);
            InvokeControl(close);
            closes++;
            await Task.Delay(Math.Max(150, _options.PollingIntervalMs / 2), ct);

            try
            {
                await WaitUntilUiAsync(
                    () => IsProcessManagerVisible() || DetectReplicaScreenName() != before,
                    TimeSpan.FromSeconds(12),
                    $"screen change after InternalWindow_Close ({before})", ct);
            }
            catch (WaitTimeoutException)
            {
                _logger.LogWarning("InternalWindow_Close from {Screen} did not change the screen; retrying.", before);
            }
        }

        throw new WilkenAutomationException("RETURN_TO_PROCESS_MANAGER_TIMEOUT",
            "Could not return to Prozesse verwalten using InternalWindow_Close. ProcessManager_Grid was not visible.");
    }

    /// <summary>
    /// Find by AutomationId without walking WPF DataGrid cells. A full descendant
    /// search of Spool_Grid / ProcessManager_Grid can take tens of seconds and made
    /// InternalWindow_Close look like it had frozen on Gitterbox-Export.
    /// </summary>
    private AutomationElement? TryFindByAutomationId(string automationId)
    {
        var enterGridRows = automationId.StartsWith("ProcessRow_", StringComparison.OrdinalIgnoreCase)
            || automationId.StartsWith("Spool_Row_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(automationId, "Spool_LatestDataRow", StringComparison.OrdinalIgnoreCase);

        foreach (var root in SearchRoots())
        {
            try
            {
                var hit = FindByIdSkipGridCells(root, automationId, enterGridRows);
                if (hit is not null) return hit;
            }
            catch { }
        }
        return null;
    }

    private static AutomationElement? FindByIdSkipGridCells(
        AutomationElement root, string automationId, bool enterGridRows)
    {
        try
        {
            if (string.Equals(root.AutomationId, automationId, StringComparison.Ordinal))
                return root;
        }
        catch { }

        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            AutomationElement[] children;
            try { children = current.FindAllChildren(); }
            catch { continue; }

            foreach (var child in children)
            {
                string? childId = null;
                string? className = null;
                try { childId = child.AutomationId; }
                catch { }
                if (string.Equals(childId, automationId, StringComparison.Ordinal))
                    return child;

                try { className = child.ClassName; }
                catch { }

                var isGrid = className is "DataGrid" or "ListView";
                if (isGrid && !enterGridRows)
                    continue;

                queue.Enqueue(child);
            }
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
                var name = SafeUiName(node);
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
