using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace WilkenAutomation.Worker.Windows;

/// <summary>
/// Control-discovery POC (Phase 5): dumps the UIA tree of a window so Wilken CS/2
/// controls (AutomationId, Name, ClassName, ControlType, handle, enabled state)
/// can be identified and mapped into Wilken:Selectors configuration.
/// Run with: dotnet run -- --inspect "Window Title"
/// </summary>
public static class UiaTreeDumper
{
    public static string DumpWindowByTitle(string titleContains, int maxDepth = 8)
    {
        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var window = desktop.FindAllChildren()
            .FirstOrDefault(w => (w.Properties.Name.ValueOrDefault ?? "")
                .Contains(titleContains, StringComparison.OrdinalIgnoreCase));

        if (window is null)
        {
            var titles = desktop.FindAllChildren()
                .Select(w => w.Properties.Name.ValueOrDefault)
                .Where(n => !string.IsNullOrWhiteSpace(n));
            return $"No top-level window matching '{titleContains}' found.\nOpen windows:\n  " +
                   string.Join("\n  ", titles);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"UIA control tree for window '{window.Properties.Name.ValueOrDefault}'");
        sb.AppendLine($"Captured: {DateTime.Now:O}");
        sb.AppendLine(new string('-', 100));
        Dump(window, sb, 0, maxDepth);
        return sb.ToString();
    }

    private static void Dump(AutomationElement element, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        var indent = new string(' ', depth * 2);
        var p = element.Properties;
        sb.AppendLine(
            $"{indent}[{p.ControlType.ValueOrDefault}] " +
            $"AutomationId='{p.AutomationId.ValueOrDefault}' " +
            $"Name='{Trunc(p.Name.ValueOrDefault)}' " +
            $"Class='{p.ClassName.ValueOrDefault}' " +
            $"Handle=0x{p.NativeWindowHandle.ValueOrDefault:X} " +
            $"Enabled={p.IsEnabled.ValueOrDefault} " +
            $"Offscreen={p.IsOffscreen.ValueOrDefault}");

        foreach (var child in element.FindAllChildren())
            Dump(child, sb, depth + 1, maxDepth);
    }

    private static string Trunc(string? value) =>
        value is null ? "" : value.Length <= 60 ? value : value[..60] + "...";
}
