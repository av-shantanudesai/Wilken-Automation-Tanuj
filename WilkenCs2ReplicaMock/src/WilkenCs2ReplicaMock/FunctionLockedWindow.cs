using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace WilkenCs2ReplicaMock;

/// <summary>
/// Wilken-style "Funktion gesperrt" dialog shown when Prozesse verwalten is invoked
/// again while its existing report/list/export child workflow is still open.
/// </summary>
public sealed class FunctionLockedWindow : Window
{
    public FunctionLockedWindow()
    {
        Title = "Funktion gesperrt";
        Width = 330;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        Background = Brushes.Transparent;
        AutomationProperties.SetAutomationId(this, "FunctionLockedDialog");
        AutomationProperties.SetName(this, "Funktion gesperrt");

        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 96, 142)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(190, 214, 244))
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(48, 103, 186)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 77, 135)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 3, 0, 2) };
        var titleIcon = new Border
        {
            Width = 21,
            Height = 21,
            Background = new SolidColorBrush(Color.FromRgb(235, 73, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(170, 45, 28)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "×",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            }
        };
        title.Children.Add(titleIcon);
        title.Children.Add(new TextBlock
        {
            Text = "Funktion gesperrt",
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        });
        titleBar.Child = title;
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var body = new Grid { Margin = new Thickness(11, 8, 10, 3) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        body.ColumnDefinitions.Add(new ColumnDefinition());

        var bodyIcon = new Border
        {
            Width = 25,
            Height = 25,
            Margin = new Thickness(0, 3, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(235, 73, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(170, 45, 28)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "×",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 19,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -3, 0, 0)
            }
        };
        Grid.SetColumn(bodyIcon, 0);
        body.Children.Add(bodyIcon);

        var message = new TextBlock
        {
            Text = "Die angeforderte Funktion\n\"CAD18\"\nist derzeit gesperrt, da sie bereits\nin einem anderen Prozess verwendet wird.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Black,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetAutomationId(message, "FunctionLocked_Message");
        Grid.SetColumn(message, 1);
        body.Children.Add(message);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var ok = new Button
        {
            Content = "✓  OK",
            Width = 72,
            Height = 23,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 3, 8, 7),
            Background = new SolidColorBrush(Color.FromRgb(221, 231, 242))
        };
        AutomationProperties.SetAutomationId(ok, "FunctionLocked_OK");
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        Grid.SetRow(ok, 2);
        root.Children.Add(ok);

        frame.Child = root;
        Content = frame;
    }
}
