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
/// Spool handling: Liste anzeigen / Druckauswahl, exact data-row matching against the pre-job snapshot, and the Export - Erweitert context menu path to Gitterbox.
/// </summary>
public partial class WindowsWilkenAutomationService
{
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
}
