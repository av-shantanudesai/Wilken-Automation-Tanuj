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
/// UIA element lookup: AutomationId-first search with Wilken:Selectors and German-name fallbacks; grid-aware traversal that skips DataGrid cells unless a row id is requested.
/// </summary>
public partial class WindowsWilkenAutomationService
{
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
