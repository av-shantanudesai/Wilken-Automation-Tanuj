using System.Diagnostics;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Windows;

/// <summary>
/// Inspect-first discovery for real Wilken. The live desktop may be WinForms,
/// WPF, Win32, or Java — this dump is how selectors are chosen. Run the worker
/// inside the Citrix Test Environment session (after the user logged in):
///   dotnet run --project WilkenAutomation.Worker -- --inspect
///   dotnet run --project WilkenAutomation.Worker -- --inspect "Window Title"
/// </summary>
public static class UiaTreeDumper
{
    public static string ListTopLevelWindows()
    {
        using var automation = new UIA3Automation();
        var sb = new StringBuilder();
        sb.AppendLine("Top-level windows in this Windows session (run this inside the Citrix desktop, not in the browser PC):");
        sb.AppendLine($"Captured: {DateTime.Now:O}");
        sb.AppendLine(new string('-', 100));

        foreach (var window in automation.GetDesktop().FindAllChildren())
        {
            var title = window.Properties.Name.ValueOrDefault ?? "";
            if (string.IsNullOrWhiteSpace(title)) continue;

            var pid = window.Properties.ProcessId.ValueOrDefault;
            var processName = TryProcessName(pid);
            var cls = window.Properties.ClassName.ValueOrDefault ?? "";
            var framework = window.Properties.FrameworkId.ValueOrDefault ?? "";
            var children = SafeChildCount(window);
            var flags = Classify(processName, cls, framework, children);

            sb.AppendLine(
                $"PID {pid,-6} Process='{processName}' Framework='{framework}' Class='{cls}' Children={children} Title='{Trunc(title, 80)}'{flags}");
        }

        sb.AppendLine();
        sb.AppendLine("Next: dump one window, all windows, or watch while you click:");
        sb.AppendLine("  WilkenAutomation.Worker.exe --inspect \"<Title from the list>\"");
        sb.AppendLine("  WilkenAutomation.Worker.exe --inspect --all");
        sb.AppendLine("  WilkenAutomation.Worker.exe --inspect-watch --inspect-record");
        return sb.ToString();
    }

    public static string DumpWindowByTitle(string titleContains, int maxDepth = 16)
    {
        using var automation = new UIA3Automation();
        var matches = GetTopLevelWindows(automation)
            .Where(w => w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            return $"No top-level window matching '{titleContains}' found.\n\n" + ListTopLevelWindows();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"UIA control dump for title containing '{titleContains}' ({matches.Count} window(s)).");
        sb.AppendLine($"Captured: {DateTime.Now:O}");
        sb.AppendLine("Map AutomationId / Name / ClassName into Wilken:Selectors as AutomationId:, Name:, ClassName: or NameContains:.");
        sb.AppendLine(new string('-', 100));

        foreach (var window in matches)
            sb.Append(DumpWindow(window, maxDepth));

        return sb.ToString();
    }

    public static string DumpAllInterestingWindows(int maxDepth = 16)
    {
        using var automation = new UIA3Automation();
        var windows = GetTopLevelWindows(automation)
            .Where(w => !w.IsRemoteDisplay)
            .ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"UIA dump of {windows.Count} non-Citrix top-level window(s).");
        sb.AppendLine($"Captured: {DateTime.Now:O}");
        sb.AppendLine(new string('-', 100));
        foreach (var window in windows)
            sb.Append(DumpWindow(window, maxDepth));
        return sb.ToString();
    }

    public static List<TopLevelWindow> GetTopLevelWindows(UIA3Automation automation, bool includeUntitled = false)
    {
        var list = new List<TopLevelWindow>();
        foreach (var element in automation.GetDesktop().FindAllChildren())
        {
            var title = element.Properties.Name.ValueOrDefault ?? "";
            if (string.IsNullOrWhiteSpace(title) && !includeUntitled) continue;
            var pid = element.Properties.ProcessId.ValueOrDefault;
            var processName = TryProcessName(pid);
            var cls = element.Properties.ClassName.ValueOrDefault ?? "";
            var framework = element.Properties.FrameworkId.ValueOrDefault ?? "";
            if (string.IsNullOrWhiteSpace(title))
                title = $"(untitled) {processName} {pid}";
            list.Add(new TopLevelWindow(
                element, title, pid, processName, cls, framework,
                SafeChildCount(element),
                WilkenSessionPolicy.IsRemoteDisplayProcess(processName)));
        }
        return list;
    }

    public static string DumpWindow(TopLevelWindow window, int maxDepth = 16)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"WINDOW Title='{window.Title}' PID={window.Pid} Process='{window.ProcessName}'");
        sb.AppendLine($"  Framework='{window.Framework}' Class='{window.ClassName}' DirectChildren={window.ChildCount}");
        sb.AppendLine(Classify(window.ProcessName, window.ClassName, window.Framework, window.ChildCount).TrimStart());

        if (window.IsRemoteDisplay)
        {
            sb.AppendLine("  BLOCKED: this is a Citrix/browser remote-display window (pixels only).");
            sb.AppendLine("  Run inspect inside the published Test Environment desktop after login.");
            return sb.ToString();
        }

        if (WilkenSessionPolicy.IsLikelyJavaWindow(window.ClassName, window.Framework) && window.ChildCount < 3)
        {
            sb.AppendLine("  JAVA: control tree is almost empty. Enable Java Access Bridge in this session:");
            sb.AppendLine("    jabswitch -enable");
            sb.AppendLine("  Restart Wilken, then inspect again.");
        }
        else if (window.ChildCount < 3
                 && !string.Equals(window.Framework, "WPF", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(window.Framework, "WinForm", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("  SPARSE TREE: UIA sees almost no controls. Confirm the worker is in the same");
            sb.AppendLine("  interactive session as Wilken (not Session 0 / Windows Service, not the Citrix client PC).");
        }

        Dump(window.Element, sb, 0, maxDepth);
        return sb.ToString();
    }

    public static string WindowFingerprint(TopLevelWindow window)
    {
        var sb = new StringBuilder();
        sb.Append(window.Title).Append('|').Append(window.ProcessName).Append('|').Append(window.ChildCount);
        CollectFingerprint(window.Element, sb, 0, 10);
        return sb.ToString();
    }

    public static string PublicSuggestSelector(
        string? automationId, string? name, string? className, string? helpText = null, string? legacyName = null) =>
        SuggestSelector(automationId, name, className, helpText, legacyName);

    public readonly record struct TopLevelWindow(
        AutomationElement Element,
        string Title,
        int Pid,
        string ProcessName,
        string ClassName,
        string Framework,
        int ChildCount,
        bool IsRemoteDisplay);

    private static void CollectFingerprint(AutomationElement element, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        try
        {
            var id = element.Properties.AutomationId.ValueOrDefault ?? "";
            var name = element.Properties.Name.ValueOrDefault ?? "";
            var type = element.Properties.ControlType.ValueOrDefault.ToString();
            if (!string.IsNullOrWhiteSpace(id) || (!string.IsNullOrWhiteSpace(name) && name.Length <= 80))
                sb.Append('|').Append(type).Append(':').Append(id).Append(':').Append(Trunc(name, 40));
            foreach (var child in element.FindAllChildren())
                CollectFingerprint(child, sb, depth + 1, maxDepth);
        }
        catch { }
    }

    private static void Dump(AutomationElement element, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        var indent = new string(' ', depth * 2);
        var p = element.Properties;
        var patterns = DescribePatterns(element);
        string? help = null;
        string? value = null;
        string? legacy = null;
        try { help = p.HelpText.ValueOrDefault; } catch { }
        try
        {
            if (element.Patterns.Value.IsSupported)
                value = element.Patterns.Value.Pattern.Value.ValueOrDefault;
        }
        catch { }
        try
        {
            if (element.Patterns.LegacyIAccessible.IsSupported)
                legacy = element.Patterns.LegacyIAccessible.Pattern.Name;
        }
        catch { }

        var suggested = SuggestSelector(p.AutomationId.ValueOrDefault, p.Name.ValueOrDefault, p.ClassName.ValueOrDefault, help, legacy);
        sb.AppendLine(
            $"{indent}[{p.ControlType.ValueOrDefault}] " +
            $"AutomationId='{p.AutomationId.ValueOrDefault}' " +
            $"Name='{Trunc(p.Name.ValueOrDefault, 80)}' " +
            $"Help='{Trunc(help, 60)}' " +
            $"Legacy='{Trunc(legacy, 60)}' " +
            $"Value='{Trunc(value, 40)}' " +
            $"Class='{p.ClassName.ValueOrDefault}' " +
            $"Framework='{p.FrameworkId.ValueOrDefault}' " +
            $"Handle=0x{p.NativeWindowHandle.ValueOrDefault:X} " +
            $"Enabled={p.IsEnabled.ValueOrDefault} " +
            $"Offscreen={p.IsOffscreen.ValueOrDefault}" +
            (string.IsNullOrEmpty(patterns) ? "" : $" Patterns={patterns}") +
            (string.IsNullOrEmpty(suggested) ? "" : $" -> {suggested}"));

        foreach (var child in element.FindAllChildren())
            Dump(child, sb, depth + 1, maxDepth);
    }

    private static string SuggestSelector(
        string? automationId, string? name, string? className, string? helpText = null, string? legacyName = null)
    {
        if (!string.IsNullOrWhiteSpace(automationId))
            return $"AutomationId:{automationId}";
        if (!string.IsNullOrWhiteSpace(name) && name.Length <= 80)
            return $"Name:{name}";
        if (!string.IsNullOrWhiteSpace(legacyName) && legacyName.Length <= 80)
            return $"Name:{legacyName}";
        if (!string.IsNullOrWhiteSpace(helpText) && helpText.Length <= 80)
            return $"HelpTextContains:{helpText}";
        if (!string.IsNullOrWhiteSpace(className) && !className.StartsWith("Hwnd", StringComparison.OrdinalIgnoreCase))
            return $"ClassName:{className}";
        return "";
    }

    private static string DescribePatterns(AutomationElement element)
    {
        var names = new List<string>();
        try { if (element.Patterns.Invoke.IsSupported) names.Add("Invoke"); } catch { }
        try { if (element.Patterns.Value.IsSupported) names.Add("Value"); } catch { }
        try { if (element.Patterns.Toggle.IsSupported) names.Add("Toggle"); } catch { }
        try { if (element.Patterns.SelectionItem.IsSupported) names.Add("SelectionItem"); } catch { }
        try { if (element.Patterns.Selection.IsSupported) names.Add("Selection"); } catch { }
        try { if (element.Patterns.ExpandCollapse.IsSupported) names.Add("ExpandCollapse"); } catch { }
        try { if (element.Patterns.LegacyIAccessible.IsSupported) names.Add("LegacyIAccessible"); } catch { }
        return names.Count == 0 ? "" : string.Join(",", names);
    }

    private static string Classify(string processName, string className, string framework, int children)
    {
        if (WilkenSessionPolicy.IsRemoteDisplayProcess(processName))
            return "  [Citrix/browser remote display — cannot automate]";
        if (WilkenSessionPolicy.IsLikelyJavaWindow(className, framework))
            return "  [Java — enable Java Access Bridge if the tree is empty]";
        if (framework.Equals("WPF", StringComparison.OrdinalIgnoreCase))
            return "  [WPF]";
        if (framework.Equals("WinForm", StringComparison.OrdinalIgnoreCase))
            return "  [WinForms]";
        if (framework.Equals("Win32", StringComparison.OrdinalIgnoreCase) && children > 0)
            return "  [Win32]";
        return "";
    }

    private static int SafeChildCount(AutomationElement window)
    {
        try { return window.FindAllChildren().Length; }
        catch { return 0; }
    }

    private static string TryProcessName(int pid)
    {
        if (pid <= 0) return "";
        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return ""; }
    }

    private static string Trunc(string? value, int max) =>
        value is null ? "" : value.Length <= max ? value : value[..max] + "...";
}
