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
        sb.AppendLine("Next: open Wilken, log in, then dump a window:");
        sb.AppendLine("  dotnet run --project WilkenAutomation.Worker -- --inspect \"<Title from the list>\"");
        return sb.ToString();
    }

    public static string DumpWindowByTitle(string titleContains, int maxDepth = 10)
    {
        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var matches = desktop.FindAllChildren()
            .Where(w => (w.Properties.Name.ValueOrDefault ?? "")
                .Contains(titleContains, StringComparison.OrdinalIgnoreCase))
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
        {
            var pid = window.Properties.ProcessId.ValueOrDefault;
            var processName = TryProcessName(pid);
            var cls = window.Properties.ClassName.ValueOrDefault ?? "";
            var framework = window.Properties.FrameworkId.ValueOrDefault ?? "";
            var children = SafeChildCount(window);

            sb.AppendLine();
            sb.AppendLine($"WINDOW Title='{window.Properties.Name.ValueOrDefault}' PID={pid} Process='{processName}'");
            sb.AppendLine($"  Framework='{framework}' Class='{cls}' DirectChildren={children}");
            sb.AppendLine(Classify(processName, cls, framework, children).TrimStart());

            if (WilkenSessionPolicy.IsRemoteDisplayProcess(processName))
            {
                sb.AppendLine("  BLOCKED: this is a Citrix/browser remote-display window (pixels only).");
                sb.AppendLine("  Run --inspect inside the published Test Environment desktop after login.");
                continue;
            }

            if (WilkenSessionPolicy.IsLikelyJavaWindow(cls, framework) && children < 3)
            {
                sb.AppendLine("  JAVA: control tree is almost empty. Enable Java Access Bridge in this session:");
                sb.AppendLine("    jabswitch -enable");
                sb.AppendLine("  Restart Wilken, then run --inspect again.");
            }
            else if (children < 3 && !string.Equals(framework, "WPF", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(framework, "WinForm", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("  SPARSE TREE: UIA sees almost no controls. Confirm the worker is in the same");
                sb.AppendLine("  interactive session as Wilken (not Session 0 / Windows Service, not the Citrix client PC).");
            }

            Dump(window, sb, 0, maxDepth);
        }

        return sb.ToString();
    }

    private static void Dump(AutomationElement element, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        var indent = new string(' ', depth * 2);
        var p = element.Properties;
        var patterns = DescribePatterns(element);
        var suggested = SuggestSelector(p.AutomationId.ValueOrDefault, p.Name.ValueOrDefault, p.ClassName.ValueOrDefault);
        sb.AppendLine(
            $"{indent}[{p.ControlType.ValueOrDefault}] " +
            $"AutomationId='{p.AutomationId.ValueOrDefault}' " +
            $"Name='{Trunc(p.Name.ValueOrDefault, 60)}' " +
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

    private static string SuggestSelector(string? automationId, string? name, string? className)
    {
        if (!string.IsNullOrWhiteSpace(automationId))
            return $"AutomationId:{automationId}";
        if (!string.IsNullOrWhiteSpace(name) && name.Length <= 80)
            return $"Name:{name}";
        if (!string.IsNullOrWhiteSpace(className) && !className.StartsWith("Hwnd", StringComparison.OrdinalIgnoreCase))
            return $"ClassName:{className}";
        return "";
    }

    private static string DescribePatterns(AutomationElement element)
    {
        var names = new List<string>();
        try { if (element.Patterns.Invoke.IsSupported) names.Add("Invoke"); } catch { }
        try { if (element.Patterns.Value.IsSupported) names.Add("Value"); } catch { }
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
