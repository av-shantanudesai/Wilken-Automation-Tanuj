using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WilkenCs2ReplicaMock;

/// <summary>
/// Druckauswahl panel hosted inside the same work area as Liste anzeigen /
/// Prozesse verwalten. It is not a separate OS window.
/// </summary>
public sealed class PrintSelectionWindow : UserControl
{
    public event EventHandler? Started;
    public event EventHandler? Cancelled;

    public PrintSelectionWindow()
    {
        MinWidth = 980;
        Background = new SolidColorBrush(Color.FromRgb(184, 211, 246));
        AutomationProperties.SetAutomationId(this, "PrintSelectionDialog");
        AutomationProperties.SetName(this, "Druckauswahl");

        var root = new Grid { Margin = new Thickness(36, 24, 36, 18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        for (var i = 0; i < 13; i++) root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });

        AddHeader(root, "Sortierung", 0); AddHeader(root, "Von/nur Wert", 1); AddHeader(root, "Bis Wert", 2); AddHeader(root, "Auswahl", 3); AddHeader(root, "E/A", 4);
        var labels = new[] { "Konzern", "Mandant", "Werk", "Version/Release", "Gebiet", "Listenname", "Programmname", "Terminal", "Drucker", "Erstelldatum", "Status", "Benutzer", "Listenbezeichnung" };

        for (var i = 0; i < labels.Length; i++)
        {
            var radio = new RadioButton { Content = labels[i], IsChecked = i == 5, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center, GroupName = "sort" };
            AutomationProperties.SetAutomationId(radio, $"PrintSelection_Sort_{i}"); Grid.SetColumn(radio, 0); Grid.SetRow(radio, i + 1); root.Children.Add(radio);

            FrameworkElement fromControl;
            if (i == 3)
            {
                var version = new StackPanel { Orientation = Orientation.Horizontal };
                var v = new TextBox { Text = "3", Width = 45 }; var r = new TextBox { Text = "0", Width = 45 };
                AutomationProperties.SetAutomationId(v, "PrintSelection_From_Version"); AutomationProperties.SetAutomationId(r, "PrintSelection_From_Release");
                version.Children.Add(v); version.Children.Add(r); fromControl = version;
            }
            else
            {
                var from = new TextBox { Text = i switch { 0 => "1", 1 => "02", 4 => "CSA", 11 => "BHL", _ => "" } };
                AutomationProperties.SetAutomationId(from, $"PrintSelection_From_{labels[i].Replace('/', '_')}"); fromControl = from;
            }
            Grid.SetColumn(fromControl, 1); Grid.SetRow(fromControl, i + 1); root.Children.Add(fromControl);

            var to = new TextBox(); AutomationProperties.SetAutomationId(to, $"PrintSelection_To_{i}"); Grid.SetColumn(to, 2); Grid.SetRow(to, i + 1); root.Children.Add(to);
            var selWrap = ArrowBox(i is 0 or 1 or 3 or 4 or 11 ? "1" : "", $"PrintSelection_Select_{i}"); Grid.SetColumn(selWrap, 3); Grid.SetRow(selWrap, i + 1); root.Children.Add(selWrap);
            var eaWrap = ArrowBox(i is 0 or 1 or 3 or 4 or 11 ? "E" : "", $"PrintSelection_EA_{i}"); Grid.SetColumn(eaWrap, 4); Grid.SetRow(eaWrap, i + 1); root.Children.Add(eaWrap);
        }

        var all = new CheckBox { Content = "Alle anzeigen", Margin = new Thickness(4) };
        AutomationProperties.SetAutomationId(all, "PrintSelection_All"); Grid.SetColumn(all, 0); Grid.SetRow(all, 14); root.Children.Add(all);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var start = new Button { Content = "Start", Width = 190, Margin = new Thickness(0, 0, 45, 0) };
        AutomationProperties.SetAutomationId(start, "PrintSelection_Start");
        start.Click += (_, _) => Started?.Invoke(this, EventArgs.Empty);
        var cancel = new Button { Content = "Abbrechen", Width = 190 };
        AutomationProperties.SetAutomationId(cancel, "PrintSelection_Cancel");
        cancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        buttons.Children.Add(start); buttons.Children.Add(cancel); Grid.SetColumn(buttons, 0); Grid.SetColumnSpan(buttons, 3); Grid.SetRow(buttons, 15); root.Children.Add(buttons);
        Content = root;
    }

    private static Grid ArrowBox(string text, string id)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        var box = new TextBox { Text = text }; AutomationProperties.SetAutomationId(box, id); g.Children.Add(box);
        var arrow = new Button { Content = "›", Padding = new Thickness(0) }; Grid.SetColumn(arrow, 1); g.Children.Add(arrow); return g;
    }

    private static void AddHeader(Grid grid, string text, int col)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4), Foreground = new SolidColorBrush(Color.FromRgb(38, 64, 107)) };
        Grid.SetColumn(t, col); Grid.SetRow(t, 0); grid.Children.Add(t);
    }
}
