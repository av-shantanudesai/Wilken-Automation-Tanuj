// Generic UI helpers shared across screens: AutomationId setter, common control factories, visual-tree lookup.
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WilkenCs2ReplicaMock;

public partial class MainWindow
{
    private static void AutomationId(DependencyObject control, string id) => AutomationProperties.SetAutomationId(control, id);

    private GroupBox Group(string header)
    {
        return new GroupBox
        {
            Header = header,
            Margin = new Thickness(9, 7, 9, 7),
            Padding = new Thickness(10, 8, 8, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(126, 149, 180)),
            Foreground = new SolidColorBrush(Color.FromRgb(43, 72, 121)),
            Background = Brushes.Transparent
        };
    }

    private static CheckBox Check(string text, bool value, string id)
    {
        var c = new CheckBox { Content = text, IsChecked = value };
        AutomationId(c, id); return c;
    }

    private static Button HiddenAutomationButton(string id, string name, Action click)
    {
        var button = new Button
        {
            Width = 16,
            Height = 22,
            Opacity = 0.15,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 14, 0, 8),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = true,
            IsTabStop = false
        };
        AutomationId(button, id);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private static FrameworkElement Row(string label, string text, string? id = null, bool enabled = true)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        p.Children.Add(new TextBlock { Text = label, Width = 200, VerticalAlignment = VerticalAlignment.Center });
        var b = new TextBox { Text = text, Width = 165, IsReadOnly = !enabled, Background = enabled ? Brushes.White : new SolidColorBrush(Color.FromRgb(218, 230, 246)) };
        if (id is not null) AutomationId(b, id); p.Children.Add(b); return p;
    }

    private static T? FindByAutomationId<T>(DependencyObject? root, string id) where T : DependencyObject
    {
        if (root is null) return null;
        if (root is T t && AutomationProperties.GetAutomationId(t) == id) return t;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindByAutomationId<T>(VisualTreeHelper.GetChild(root, i), id);
            if (found is not null) return found;
        }
        return null;
    }
}
