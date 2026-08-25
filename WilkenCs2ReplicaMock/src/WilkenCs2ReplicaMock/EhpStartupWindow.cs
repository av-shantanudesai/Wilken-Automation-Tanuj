using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WilkenCs2ReplicaMock;

/// <summary>
/// Real-test first dialog: Country / Company, then Start Wilken.
/// The worker does not fill this — the user does.
/// </summary>
public sealed class EhpStartupWindow : Window
{
    public EhpStartupWindow()
    {
        Title = "EHP 2 - Wilken Edition";
        Width = 420;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        AutomationProperties.SetAutomationId(this, "EhpStartupDialog");
        AutomationProperties.SetName(this, "EHP 2 - Wilken Edition");

        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 5; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        AddLabel(root, "Country", 0);
        var country = new ComboBox { ItemsSource = new[] { "DE", "BE", "NL", "DK", "FR", "ES", "PT", "PL" }, SelectedIndex = 0 };
        AutomationProperties.SetAutomationId(country, "Ehp_Country");
        Grid.SetColumn(country, 1); Grid.SetRow(country, 0); root.Children.Add(country);

        AddLabel(root, "Company", 1);
        var company = new ComboBox
        {
            ItemsSource = new[] { "OHG Einkauf", "OHG2 Testumgebung", "Berlin Nord", "Essen" },
            SelectedIndex = 0
        };
        AutomationProperties.SetAutomationId(company, "Ehp_Company");
        Grid.SetColumn(company, 1); Grid.SetRow(company, 1); root.Children.Add(company);

        AddLabel(root, "Description", 2);
        var description = new TextBox { IsReadOnly = true };
        AutomationProperties.SetAutomationId(description, "Ehp_Description");
        Grid.SetColumn(description, 1); Grid.SetRow(description, 2); root.Children.Add(description);

        var useTest = new CheckBox { Content = "Use Test Systems", Margin = new Thickness(0, 12, 0, 8) };
        AutomationProperties.SetAutomationId(useTest, "Ehp_UseTestSystems");
        Grid.SetColumn(useTest, 1); Grid.SetRow(useTest, 3); root.Children.Add(useTest);

        var saveDefault = new Button { Content = "Save Default", Width = 120, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetAutomationId(saveDefault, "Ehp_SaveDefault");
        Grid.SetColumn(saveDefault, 1); Grid.SetRow(saveDefault, 4); root.Children.Add(saveDefault);

        var buttons = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new Button { Content = "Cancel", Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(cancel, "Ehp_Cancel");
        cancel.Click += (_, _) => Close();
        var start = new Button { Content = "Start Wilken", Width = 130, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(start, "Ehp_StartWilken");
        start.Click += (_, _) =>
        {
            new LoginWindow().Show();
            Close();
        };
        Grid.SetColumn(cancel, 0); buttons.Children.Add(cancel);
        Grid.SetColumn(start, 1); buttons.Children.Add(start);
        Grid.SetColumnSpan(buttons, 2); Grid.SetRow(buttons, 5); root.Children.Add(buttons);

        Content = root;
    }

    private static void AddLabel(Grid root, string text, int row)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
        Grid.SetRow(label, row); root.Children.Add(label);
    }
}
