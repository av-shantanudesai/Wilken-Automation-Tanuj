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
/// CS/2 workflow steps: open the saved process, verify static fields, set Zeitraum and Fachbereich, save + Ausfuehren, wait for the Fortschritt dialog, and unwind back to Prozesse verwalten.
/// </summary>
public partial class WindowsWilkenAutomationService
{
    private ExportDefinition ReplicaDefinition =>
        _catalog.ResolveForJob(_job ?? new ExportJob { Department = "Handelsrecht" });

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
}
