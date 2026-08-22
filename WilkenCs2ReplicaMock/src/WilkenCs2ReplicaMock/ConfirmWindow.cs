using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WilkenCs2ReplicaMock;

public sealed class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string text, string automationId = "ConfirmDialog")
    {
        Title = title;
        Width = 500;
        Height = 155;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(190, 214, 244));
        AutomationProperties.SetAutomationId(this, automationId);

        var root = new StackPanel { Margin = new Thickness(55, 24, 55, 14) };
        root.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 20), HorizontalAlignment = HorizontalAlignment.Center });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var yes = new Button { Content = "Ja", Width = 135, Margin = new Thickness(15, 0, 15, 0) };
        AutomationProperties.SetAutomationId(yes, "Confirm_Yes");
        yes.Click += (_, _) => { DialogResult = true; Close(); };
        var cancel = new Button { Content = "Abbrechen", Width = 135, Margin = new Thickness(15, 0, 15, 0) };
        AutomationProperties.SetAutomationId(cancel, "Confirm_Cancel");
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(yes); buttons.Children.Add(cancel); root.Children.Add(buttons); Content = root;
    }
}
