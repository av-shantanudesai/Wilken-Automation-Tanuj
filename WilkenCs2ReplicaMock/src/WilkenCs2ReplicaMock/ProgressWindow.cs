using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WilkenCs2ReplicaMock;

public sealed class ProgressWindow : Window
{
    private readonly TextBlock _message;
    private readonly ProgressBar _progress;

    public ProgressWindow()
    {
        Title = "Fortschritt";
        Width = 390;
        Height = 118;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(190, 214, 244));
        AutomationProperties.SetAutomationId(this, "ProgressDialog");
        var root = new StackPanel { Margin = new Thickness(7) };
        _message = new TextBlock { Margin = new Thickness(3, 2, 3, 3) };
        AutomationProperties.SetAutomationId(_message, "Progress_Message");
        _progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(3, 0, 3, 4), Foreground = Brushes.Green };
        AutomationProperties.SetAutomationId(_progress, "Progress_Bar");
        var cancel = new Button { Content = "Abbrechen", Width = 95, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(cancel, "Progress_Cancel");
        root.Children.Add(_message); root.Children.Add(_progress); root.Children.Add(cancel); Content = root;
    }

    public void SetMessage(string message) => _message.Text = message;
    public void SetProgress(int value) => _progress.Value = Math.Clamp(value, 0, 100);
}
