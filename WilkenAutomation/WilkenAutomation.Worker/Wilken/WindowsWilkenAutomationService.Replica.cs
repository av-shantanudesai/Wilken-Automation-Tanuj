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
/// CS/2 workflow used by WilkenCs2ReplicaMock and real Test Wilken (attach-only):
/// Prozesse verwalten → select process → save → execute → wait Fortschritt → Liste anzeigen →
/// Druckauswahl → exact data spool row → Export Erweitert → force XLSX → validate →
/// InternalWindow_Close until ProcessManager_Grid (never re-click Prozesse verwalten).
/// Replica AutomationIds are tried first, then Wilken:Selectors, then German names.
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
        var expectedTitle = definition.ReplicaTitleContains ?? "Anlagenspiegel erstellen";
        await WaitUntilUiAsync(
            () =>
            {
                if (!ScreenTitleContains(expectedTitle)
                    && !ScreenTitleContains(name)
                    && !ScreenTitleContains("erstellen"))
                    return false;
                if (IsReplica || TryFindByAutomationId(processField) is not null)
                    return ControlShowsValue(TryFindByAutomationId(processField), number)
                           && ControlShowsValue(TryFindByAutomationId(nameField), name);
                return true;
            },
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
            var rows = CollectGridRows(grid);
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
        if (!IsReplica)
        {
            _logger.LogInformation(
                "Real Wilken: skip replica-only static field IDs; Zeitraum and Fachbereich are still set.");
            return;
        }

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
        // Real German Wilken: von/bis dates plus Zeitraum 01/year–12/year.
        // Replica Zugangsliste has both; Anlagenspiegel has Zeitraum only.
        if (IsZugangDefinition || !IsReplica)
        {
            await SetIdValueAsync("Zugang_DateFrom", $"01.01.{fiscalYear}", ct);
            await SetIdValueAsync("Zugang_DateTo", $"31.12.{fiscalYear}", ct);
        }

        var prefix = IsZugangDefinition ? "Zugang_Period" : "Anlage_Period";
        await SetIdValueAsync($"{prefix}_0", "01", ct);
        await SetIdValueAsync($"{prefix}_1", fiscalYear.ToString(), ct);
        await SetIdValueAsync($"{prefix}_2", "12", ct);
        await SetIdValueAsync($"{prefix}_3", fiscalYear.ToString(), ct);
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

        // After Zeitraum / Fachbereich are set: Speichern (floppy disk) first,
        // wait for "Prozess aktualisiert", then Ausführen (green tick).
        var save = TryFindByAutomationId("Toolbar_Save");
        if (save is not null)
        {
            InvokeControl(save);
            try
            {
                await WaitUntilUiAsync(
                    () => ScreenStatusContains("Prozess aktualisiert"),
                    TimeSpan.FromSeconds(Math.Min(_options.NavigationTimeoutSeconds, 15)),
                    "status 'Prozess aktualisiert' after save", ct);
                _logger.LogInformation("Process saved (Prozess aktualisiert).");
            }
            catch (WaitTimeoutException)
            {
                _logger.LogWarning("Save clicked; 'Prozess aktualisiert' was not seen. Continuing to Ausführen.");
            }
        }

        InvokeControl(FindByAutomationId("Toolbar_Execute"));
        try
        {
            await WaitUntilUiAsync(
                () => TryFindByAutomationId("Confirm_Yes") is not null,
                TimeSpan.FromSeconds(IsReplica ? _options.NavigationTimeoutSeconds : 8),
                "confirmation dialog", ct);
            InvokeControl(FindByAutomationId("Confirm_Yes"));
        }
        catch (WaitTimeoutException)
        {
            if (IsReplica)
                throw;
            _logger.LogWarning("No Ja/Yes confirmation after Ausführen; continuing (real Wilken may skip it).");
        }
    }

    private async Task ReplicaWaitForProgressAsync(CancellationToken ct)
    {
        try
        {
            await WaitUntilUiAsync(
                () => TryFindByAutomationId("ProgressDialog") is not null,
                TimeSpan.FromSeconds(IsReplica ? _options.NavigationTimeoutSeconds : 20),
                "Fortschritt dialog", ct);
        }
        catch (WaitTimeoutException)
        {
            if (IsReplica)
                throw;
            _logger.LogWarning("Fortschritt dialog was not found; continuing (run may have finished immediately).");
            SessionStatus = WilkenSessionStatus.Ready;
            return;
        }

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
            rows = CollectGridRows(grid);
        }
        catch
        {
            return null;
        }

        if (rows.Length == 0) return null;

        var hits = ScanSpoolHits(rows, match, requireUser: true, requireListName: true);
        if (hits.Count == 0 && !IsReplica)
        {
            _logger.LogInformation("No spool row matched replica user/list filters; retrying with report name only.");
            hits = ScanSpoolHits(rows, match, requireUser: false, requireListName: true);
        }
        if (hits.Count == 0 && !IsReplica)
            hits = ScanSpoolHits(rows, match, requireUser: false, requireListName: false);

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

    private List<(AutomationElement Row, DateTime Created)> ScanSpoolHits(
        AutomationElement[] rows, SpoolMatchSpec? match, bool requireUser, bool requireListName)
    {
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
            if (requireListName && !string.IsNullOrEmpty(listName) && !text.Contains(listName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(extension) && !text.Contains(extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (requireUser && !text.Contains(user, StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Contains("PRT", StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(extension) || !string.Equals(extension, "PRT", StringComparison.OrdinalIgnoreCase)))
                continue;

            var nextText = i + 1 < rows.Length ? ReadSubtree(rows[i + 1]) : "";
            if (nextText.Contains("Protokoll:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!nextText.Contains(report, StringComparison.OrdinalIgnoreCase)
                && !text.Contains(report, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrEmpty(exclude)
                && nextText.Contains(exclude, StringComparison.OrdinalIgnoreCase)
                && !report.Contains(exclude, StringComparison.OrdinalIgnoreCase))
                continue;

            var created = ParseSpoolTimestamp(text);
            if (created != DateTime.MinValue && created < _runStartedAtUtc.AddMinutes(-2))
                continue;
            if (_spoolSnapshot.Contains(SpoolRowKey(text, nextText))) continue;
            hits.Add((rows[i], created == DateTime.MinValue ? DateTime.UtcNow : created));
        }

        return hits;
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
            var rows = CollectGridRows(grid);
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

    private static readonly TimeSpan FastUiPoll = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// Recorded Wilken path: context menu on the selected spool row → last item
    /// (Export) → last submenu item (Erweitert) → System - Gitterbox-Export.
    /// </summary>
    private async Task ReplicaOpenGitterboxViaContextMenuAsync(CancellationToken ct)
    {
        GuardHealthy();
        if (HasExactId("Export_Target_Excel") && HasExactId("Export_Records_All"))
            return;

        OpenSpoolContextMenu();
        try
        {
            await WaitUntilUiAsync(
                () => FindOpenContextMenuItems().Count > 0
                      || HasExactId("Spool_Context_Export"),
                TimeSpan.FromSeconds(5),
                "spool context menu", ct, FastUiPoll, handleDialogs: false);

            var topItems = FindOpenContextMenuItems();
            var export = topItems.Count > 0
                ? topItems[^1]
                : TryFindByAutomationIdExact("Spool_Context_Export", false)
                  ?? throw new WilkenAutomationException("CONTROL_NOT_FOUND",
                      "Spool context menu did not expose Export as the last item.");

            _logger.LogInformation("Spool context menu: last item '{Name}'.", SafeUiName(export));
            ExpandOrInvokeMenuItem(export);

            await WaitUntilUiAsync(
                () => MenuItemChildren(export).Count > 0
                      || HasExactId("Spool_Context_ExportAdvanced")
                      || HasExactId("Export_Target_Excel"),
                TimeSpan.FromSeconds(5),
                "Export submenu", ct, FastUiPoll, handleDialogs: false);

            if (!HasExactId("Export_Target_Excel"))
            {
                var sub = MenuItemChildren(export);
                var advanced = sub.Count > 0
                    ? sub[^1]
                    : TryFindByAutomationIdExact("Spool_Context_ExportAdvanced", false)
                      ?? throw new WilkenAutomationException("CONTROL_NOT_FOUND",
                          "Export submenu did not expose Erweitert as the last item.");
                _logger.LogInformation("Export submenu: last item '{Name}'.", SafeUiName(advanced));
                InvokeControl(advanced);
            }
        }
        catch (Exception ex) when (ex is WaitTimeoutException or WilkenAutomationException)
        {
            _logger.LogWarning(ex, "Context menu Export → Erweitert failed; retrying Shift+F10 then named Erweitert.");
            SendShiftF10();
            try
            {
                await WaitUntilUiAsync(
                    () => FindOpenContextMenuItems().Count > 0 || HasExactId("Spool_Context_Export"),
                    TimeSpan.FromSeconds(3),
                    "context menu after Shift+F10", ct, FastUiPoll, handleDialogs: false);
                var top = FindOpenContextMenuItems();
                if (top.Count > 0)
                {
                    ExpandOrInvokeMenuItem(top[^1]);
                    await Task.Delay(80, ct);
                    var sub = MenuItemChildren(top[^1]);
                    if (sub.Count > 0)
                        InvokeControl(sub[^1]);
                }
                else
                {
                    var named = TryFindByAutomationIdExact("Spool_Context_ExportAdvanced", false)
                                ?? TryFindByNameHint(Cs2ControlMap.Names["Spool_Context_ExportAdvanced"], false);
                    if (named is not null)
                        InvokeControl(named);
                }
            }
            catch (Exception retryEx) when (retryEx is WaitTimeoutException or WilkenAutomationException)
            {
                throw new WilkenAutomationException("EXPORT_SCREEN_NOT_REACHED",
                    "Could not open Gitterbox-Export via spool context menu last item → last submenu item.",
                    inner: retryEx);
            }
        }

        await WaitUntilUiAsync(
            () => DetectActiveScreen() == "GITTERBOX_EXPORT",
            TimeSpan.FromSeconds(Math.Max(8, _options.NavigationTimeoutSeconds)),
            "System - Gitterbox-Export after Export → Erweitert",
            ct, FastUiPoll, handleDialogs: false);
    }

    private void OpenSpoolContextMenu()
    {
        var opener = TryFindByAutomationIdExact("Spool_OpenContextMenu", false);
        if (opener is not null)
        {
            InvokeControl(opener);
            return;
        }

        SendShiftF10();
    }

    private void SendShiftF10()
    {
        WithoutStealingInput(() =>
            Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.F10));
    }

    private void ExpandOrInvokeMenuItem(AutomationElement item)
    {
        var expanded = false;
        WithoutStealingInput(() =>
        {
            try
            {
                if (item.Patterns.ExpandCollapse.IsSupported)
                {
                    item.Patterns.ExpandCollapse.Pattern.Expand();
                    expanded = true;
                    return;
                }
            }
            catch { }

            try
            {
                item.AsMenuItem().Expand();
                expanded = true;
            }
            catch { }
        });

        if (!expanded)
        {
            try { InvokeControl(item); }
            catch (WilkenAutomationException) { }
        }
    }

    private List<AutomationElement> FindOpenContextMenuItems()
    {
        foreach (var root in ContextMenuRoots())
        {
            try
            {
                var items = root.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem));
                if (items.Length >= 2)
                    return items.ToList();
                var nested = root.FindFirstChild(cf => cf.ByControlType(ControlType.Menu))
                             ?? root.FindFirstDescendant(cf => cf.ByControlType(ControlType.Menu));
                if (nested is not null)
                {
                    items = nested.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem));
                    if (items.Length >= 2)
                        return items.ToList();
                }
            }
            catch { }
        }

        var named = TryFindByAutomationIdExact("Spool_Context_Export", false);
        if (named?.Parent is { } parent)
        {
            try
            {
                return parent.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem)).ToList();
            }
            catch { }
        }

        return new List<AutomationElement>();
    }

    private static List<AutomationElement> MenuItemChildren(AutomationElement parent)
    {
        try
        {
            return parent.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem)).ToList();
        }
        catch
        {
            return new List<AutomationElement>();
        }
    }

    private IEnumerable<AutomationElement> ContextMenuRoots()
    {
        foreach (var root in SearchRoots())
            yield return root;
        if (_automation is null) yield break;
        AutomationElement[] children;
        try { children = _automation.GetDesktop().FindAllChildren(); }
        catch { yield break; }
        foreach (var child in children)
        {
            string cls;
            ControlType type;
            try
            {
                cls = child.ClassName ?? "";
                type = child.ControlType;
            }
            catch { continue; }
            if (type is ControlType.Menu or ControlType.ToolTip
                || cls.Contains("Popup", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("Menu", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("32768"))
                yield return child;
        }
    }

    private async Task<string> ReplicaExportAsync(ExportJob job, CancellationToken ct)
    {
        GuardHealthy();
        var before = SnapshotExportFiles();

        await ReplicaOpenGitterboxViaContextMenuAsync(ct);

        var formatId = ReplicaFormatRadioId();
        SelectRadio(formatId);
        SelectRadio("Export_Target_Excel");
        SelectRadio("Export_Records_All");
        if (IsReplica
            || TryFindByAutomationId(formatId) is not null
            || TryFindByAutomationId("Export_Target_Excel") is not null)
        {
            await WaitUntilUiAsync(
                () => (TryFindByAutomationId(formatId) is null || RadioIsSelected(formatId))
                      && (TryFindByAutomationId("Export_Target_Excel") is null || RadioIsSelected("Export_Target_Excel"))
                      && (TryFindByAutomationId("Export_Records_All") is null || RadioIsSelected("Export_Records_All")),
                TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
                $"{formatId} + Excel + Alle selected", ct);
        }

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
            produced = NewestExportSince(before);
            return produced is not null || ScreenStatusContains("Export abgeschlossen");
        },
        TimeSpan.FromMinutes(_options.ExportTimeoutMinutes),
        "exported workbook", ct);

        produced ??= NewestExportSince(before);
        if (produced is null)
            throw new WilkenAutomationException("DOWNLOAD_TIMEOUT",
                "Gitterbox export finished in the UI but no new .xlsx was found in Downloads or the export folder.");

        // File appears -> size stops changing -> file is unlocked -> only then validate.
        _lastObservedExportSize = -1;
        await WaitUntilUiAsync(
            () => FileIsStableAndUnlocked(produced!),
            TimeSpan.FromMinutes(Math.Max(1, _options.ExportTimeoutMinutes)),
            "export file size stable and unlocked", ct);

        _logger.LogInformation("CS/2 export produced {Path} for {JobId}.", produced, job.JobId);

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

    private IEnumerable<string> ExportSearchFolders()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        yield return downloads;
        if (!string.IsNullOrWhiteSpace(_exportSettings.RootDirectory))
        {
            Directory.CreateDirectory(_exportSettings.RootDirectory);
            yield return _exportSettings.RootDirectory;
        }
    }

    private string[] ExportFilePatterns() =>
        IsReplica ? ["CTLP12*.xlsx"] : ["*.xlsx", "*.xls"];

    private List<(string Path, DateTime Time)> SnapshotExportFiles()
    {
        var before = new List<(string Path, DateTime Time)>();
        foreach (var folder in ExportSearchFolders())
        {
            foreach (var pattern in ExportFilePatterns())
            {
                try
                {
                    before.AddRange(Directory.GetFiles(folder, pattern)
                        .Select(f => (Path: f, Time: File.GetLastWriteTimeUtc(f))));
                }
                catch { }
            }
        }
        return before;
    }

    private string? NewestExportSince(List<(string Path, DateTime Time)> before)
    {
        var candidates = new List<FileInfo>();
        foreach (var folder in ExportSearchFolders())
        {
            foreach (var pattern in ExportFilePatterns())
            {
                try
                {
                    candidates.AddRange(Directory.GetFiles(folder, pattern).Select(f => new FileInfo(f)));
                }
                catch { }
            }
        }

        return candidates
            .Where(f => f.Length > 0
                        && (before.All(b => b.Path != f.FullName)
                            || File.GetLastWriteTimeUtc(f.FullName) > _runStartedAtUtc.AddSeconds(-5)))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    private async Task VerifyValueAsync(string automationId, string expected, CancellationToken ct)
    {
        if (!IsReplica && TryFindByAutomationId(automationId) is null)
        {
            _logger.LogWarning("Field '{Id}' not found on real Wilken; skipping verify '{Expected}'.", automationId, expected);
            return;
        }

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
        {
            if (!IsReplica)
            {
                _logger.LogWarning("Checkbox '{Id}' not found on real Wilken; skipping.", automationId);
                return;
            }
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"CS/2 checkbox '{automationId}' was not found.");
        }

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
        var field = TryFindByAutomationId(automationId);
        if (field is null)
        {
            if (!IsReplica)
            {
                _logger.LogWarning("Field '{Id}' not found on real Wilken; skipping value '{Value}'.", automationId, value);
                return;
            }
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"CS/2 control '{automationId}' was not found.");
        }

        SetControlValue(field, value);
        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFindByAutomationId(automationId), value),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{automationId} = '{value}'", ct);
    }

    private async Task SetSelectorOrIdAsync(string automationId, string value, CancellationToken ct)
    {
        var field = TryFindByAutomationId(automationId);
        if (field is null)
        {
            if (!IsReplica)
            {
                _logger.LogWarning("Field '{Id}' not found on real Wilken; skipping value '{Value}'.", automationId, value);
                return;
            }
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"CS/2 control '{automationId}' was not found.");
        }

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
        if (UseCs2Workflow)
            return "Export_Format_XLSX";

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

    private void SelectRadio(string automationId)
    {
        var radio = TryFindByAutomationId(automationId);
        if (radio is null)
        {
            if (!IsReplica)
            {
                _logger.LogWarning("Radio '{Id}' not found on real Wilken; skipping.", automationId);
                return;
            }
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"CS/2 control '{automationId}' was not found.");
        }

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
            $"CS/2 control '{automationId}' was not found.");

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

            var childOpen = DetectActiveScreen() is "GITTERBOX_EXPORT" or "SPOOL_LIST" or "REPORT";
            var close = TryFindByAutomationIdExact("InternalWindow_Close", enterGridRows: false);
            var step = Cs2ScreenUnwind.Next(
                processManagerVisible: IsProcessManagerVisible(),
                homeWorkspaceVisible: IsHomeVisible(),
                childWindowOpen: childOpen,
                internalCloseAvailable: close is not null);

            if (step == Cs2ScreenUnwind.Step.Done)
            {
                _logger.LogInformation("Returned to Prozesse verwalten after {Closes} internal close(s).", closes);
                return;
            }

            if (step == Cs2ScreenUnwind.Step.OpenProcessManagerFromHome)
            {
                await ActivateReplicaControlAsync("Nav_ProzesseVerwalten", ct);
                await WaitUntilUiAsync(
                    () => IsProcessManagerVisible() || IsFunctionLockedVisible(),
                    TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
                    "Prozesse verwalten from home", ct, FastUiPoll, handleDialogs: false);
                if (IsFunctionLockedVisible())
                {
                    DismissFunctionLockedIfPresent();
                    continue;
                }
                return;
            }

            if (step == Cs2ScreenUnwind.Step.WaitForUi || closes >= maxCloses)
            {
                if (closes >= maxCloses)
                    break;
                await Task.Delay(FastUiPoll, ct);
                continue;
            }

            var before = DetectActiveScreen();
            var expected = Cs2ScreenUnwind.ExpectedAfterClose(before) ?? "PROCESS_MANAGER";
            _logger.LogInformation("InternalWindow_Close from {Screen} → expect {Expected}.", before, expected);
            InvokeControl(close!);
            closes++;

            try
            {
                await WaitUntilUiAsync(
                    () =>
                    {
                        var now = DetectActiveScreen();
                        return now == expected || now == "PROCESS_MANAGER" || (now != before && now != "UNKNOWN");
                    },
                    TimeSpan.FromSeconds(4),
                    $"screen {expected} after InternalWindow_Close ({before})",
                    ct, FastUiPoll, handleDialogs: false);
            }
            catch (WaitTimeoutException)
            {
                _logger.LogWarning("InternalWindow_Close from {Screen} did not reach {Expected}; retrying.", before, expected);
            }
        }

        throw new WilkenAutomationException("RETURN_TO_PROCESS_MANAGER_TIMEOUT",
            "Could not return to Prozesse verwalten using InternalWindow_Close. ProcessManager_Grid was not visible.");
    }

    /// <summary>
    /// Find by AutomationId, then Wilken:Selectors, then German names.
    /// Does not walk WPF DataGrid cells unless the id is a row id — a full descendant
    /// search of Spool_Grid / ProcessManager_Grid can take tens of seconds.
    /// </summary>
    private AutomationElement? TryFindByAutomationId(string automationId)
    {
        var enterGridRows = automationId.StartsWith("ProcessRow_", StringComparison.OrdinalIgnoreCase)
            || automationId.StartsWith("Spool_Row_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(automationId, "Spool_LatestDataRow", StringComparison.OrdinalIgnoreCase);

        var hit = TryFindByAutomationIdExact(automationId, enterGridRows);
        if (hit is not null) return hit;

        if (Cs2ControlMap.SelectorKeys.TryGetValue(automationId, out var selectorKey))
        {
            hit = TryFind(selectorKey);
            if (hit is not null) return hit;
            if (string.Equals(selectorKey, "PeriodFromYearField", StringComparison.OrdinalIgnoreCase))
            {
                hit = TryFind("FiscalYearField");
                if (hit is not null) return hit;
            }
        }

        if (Cs2ControlMap.Names.TryGetValue(automationId, out var hint))
        {
            hit = TryFindByNameHint(hint, enterGridRows);
            if (hit is not null) return hit;
        }

        if (string.Equals(automationId, "ProcessManager_Grid", StringComparison.OrdinalIgnoreCase))
            return TryFindGridNearText("Prozesse verwalten");
        if (string.Equals(automationId, "Spool_Grid", StringComparison.OrdinalIgnoreCase))
            return TryFindGridNearText("Liste anzeigen")
                ?? TryFindGridNearText("Ausgabeliste")
                ?? TryFindGridNearText("Spool");
        if (string.Equals(automationId, "Navigation_Tree", StringComparison.OrdinalIgnoreCase))
            return TryFindFirstOfType(ControlType.Tree);

        return null;
    }

    private AutomationElement? TryFindByAutomationIdExact(string automationId, bool enterGridRows)
    {
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

    private AutomationElement? TryFindByNameHint(Cs2NameHint hint, bool enterGridRows)
    {
        foreach (var root in SearchRoots())
        {
            try
            {
                var hit = FindByNameSkipGridCells(root, hint, enterGridRows);
                if (hit is not null) return hit;
            }
            catch { }
        }
        return null;
    }

    private static AutomationElement? FindByNameSkipGridCells(
        AutomationElement root, Cs2NameHint hint, bool enterGridRows)
    {
        AutomationElement? best = null;
        var bestScore = 0;
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited < 4000)
        {
            visited++;
            var current = queue.Dequeue();
            var score = NameHintScore(current, hint);
            if (score > bestScore)
            {
                bestScore = score;
                best = current;
                if (score >= 200) return current;
            }

            AutomationElement[] children;
            try { children = current.FindAllChildren(); }
            catch { continue; }

            foreach (var child in children)
            {
                string? className = null;
                try { className = child.ClassName; }
                catch { }
                if ((className is "DataGrid" or "ListView") && !enterGridRows)
                    continue;
                queue.Enqueue(child);
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static int NameHintScore(AutomationElement element, Cs2NameHint hint)
    {
        try
        {
            if (hint.SkipWindowTitleBar && IsOnWindowTitleBar(element))
                return 0;
            if (hint.Types is { Length: > 0 } && Array.IndexOf(hint.Types, element.ControlType) < 0)
                return 0;

            var text = ControlSearchText(element);
            if (string.IsNullOrWhiteSpace(text)) return 0;

            foreach (var needle in hint.Names)
            {
                if (hint.ExactName)
                {
                    if (text.Equals(needle, StringComparison.OrdinalIgnoreCase))
                        return 200;
                    continue;
                }

                if (text.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return 180;
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return 100;
            }
        }
        catch { }
        return 0;
    }

    private static string ControlSearchText(AutomationElement element)
    {
        var parts = new List<string>(3);
        try
        {
            var name = element.Name;
            if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
        }
        catch { }
        try
        {
            var help = element.Properties.HelpText.ValueOrDefault;
            if (!string.IsNullOrWhiteSpace(help)) parts.Add(help);
        }
        catch { }
        try
        {
            var value = element.Patterns.Value.IsSupported
                ? element.Patterns.Value.Pattern.Value.ValueOrDefault
                : null;
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(value);
        }
        catch { }
        return string.Join(" ", parts);
    }

    private static bool IsOnWindowTitleBar(AutomationElement element)
    {
        try
        {
            var current = element;
            for (var i = 0; i < 8 && current is not null; i++)
            {
                if (current.ControlType == ControlType.TitleBar)
                    return true;
                current = current.Parent;
            }
        }
        catch { }
        return false;
    }

    private AutomationElement? TryFindGridNearText(string marker)
    {
        var grids = new List<AutomationElement>();
        foreach (var root in SearchRoots())
            CollectGrids(root, grids);

        AutomationElement? marked = null;
        var workArea = new List<AutomationElement>();
        foreach (var grid in grids)
        {
            if (IsInsideTree(grid)) continue;
            workArea.Add(grid);
            var blob = ControlSearchText(grid) + " " + ReadSubtree(grid);
            if (blob.Contains(marker, StringComparison.OrdinalIgnoreCase))
                marked = grid;
        }
        if (marked is not null) return marked;
        if (workArea.Count == 1) return workArea[0];
        return null;
    }

    private static void CollectGrids(AutomationElement root, List<AutomationElement> into)
    {
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited < 3000)
        {
            visited++;
            var current = queue.Dequeue();
            try
            {
                var type = current.ControlType;
                var className = current.ClassName ?? "";
                if (type is ControlType.DataGrid or ControlType.Table or ControlType.List
                    || className is "DataGrid" or "ListView")
                    into.Add(current);
            }
            catch { }

            AutomationElement[] children;
            try { children = current.FindAllChildren(); }
            catch { continue; }
            foreach (var child in children)
                queue.Enqueue(child);
        }
    }

    private static bool IsInsideTree(AutomationElement element)
    {
        try
        {
            var current = element.Parent;
            for (var i = 0; i < 10 && current is not null; i++)
            {
                if (current.ControlType == ControlType.Tree)
                    return true;
                current = current.Parent;
            }
        }
        catch { }
        return false;
    }

    private AutomationElement? TryFindFirstOfType(ControlType type)
    {
        foreach (var root in SearchRoots())
        {
            try
            {
                var hit = root.FindFirstDescendant(cf => cf.ByControlType(type));
                if (hit is not null) return hit;
            }
            catch { }
        }
        return null;
    }

    private static AutomationElement[] CollectGridRows(AutomationElement grid)
    {
        try
        {
            var rows = grid.FindAllDescendants(cf => cf.ByClassName("DataGridRow"));
            if (rows.Length > 0) return rows;
            rows = grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
            if (rows.Length > 0) return rows;
            rows = grid.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            if (rows.Length > 0) return rows;
            var children = grid.FindAllChildren()
                .Where(c =>
                {
                    try { return c.ControlType is not ControlType.Header and not ControlType.HeaderItem and not ControlType.ScrollBar; }
                    catch { return true; }
                })
                .ToArray();
            return children;
        }
        catch
        {
            return [];
        }
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
