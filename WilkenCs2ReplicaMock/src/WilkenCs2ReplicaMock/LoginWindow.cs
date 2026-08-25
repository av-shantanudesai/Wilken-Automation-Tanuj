using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WilkenCs2ReplicaMock;

/// <summary>
/// Real-test Anmeldung. Username, password, Konzern, and Mandant are filled by
/// the user. The dashboard Mandant is not typed here by automation.
/// </summary>
public sealed class LoginWindow : Window
{
    public LoginWindow()
    {
        Title = "Anmeldung - Wilken CS/2 Finanzmanagement";
        Width = 520;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(232, 242, 252));
        AutomationProperties.SetAutomationId(this, "LoginDialog");
        AutomationProperties.SetName(this, "Anmeldung");

        var root = new Grid { Margin = new Thickness(18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        var labels = new[] { "Benutzer", "Passwort", "Konzern", "Mandant", "Werk", "Direkteinstieg", "Anmeldedatum" };
        var ids = new[] { "Login_Benutzer", "Login_Passwort", "Login_Konzern", "Login_Mandant", "Login_Werk", "Login_Direkteinstieg", "Login_Anmeldedatum" };
        for (var i = 0; i < labels.Length; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

        for (var i = 0; i < labels.Length; i++)
        {
            var label = new TextBlock { Text = labels[i], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, i); root.Children.Add(label);
            var box = new TextBox();
            if (i == 1)
            {
                var pwd = new PasswordBox();
                AutomationProperties.SetAutomationId(pwd, ids[i]);
                Grid.SetColumn(pwd, 1); Grid.SetRow(pwd, i); root.Children.Add(pwd);
            }
            else
            {
                box.Text = i switch { 0 => "BHL", 2 => "1", 3 => "02", _ => "" };
                AutomationProperties.SetAutomationId(box, ids[i]);
                Grid.SetColumn(box, 1); Grid.SetRow(box, i); root.Children.Add(box);
            }
        }

        var actions = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        var login = new Button { Content = "Anmelden", Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetAutomationId(login, "Login_Anmelden");
        login.Click += (_, _) =>
        {
            new MainWindow().Show();
            Close();
        };
        var logout = new Button { Content = "Abmelden", Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetAutomationId(logout, "Login_Abmelden");
        logout.Click += (_, _) => Close();
        var newPwd = new Button { Content = "Neues Passwort", Height = 26 };
        AutomationProperties.SetAutomationId(newPwd, "Login_NeuesPasswort");
        actions.Children.Add(login);
        actions.Children.Add(logout);
        actions.Children.Add(newPwd);
        Grid.SetColumn(actions, 2); Grid.SetRow(actions, 0); Grid.SetRowSpan(actions, 7); root.Children.Add(actions);

        var version = new TextBlock
        {
            Text = "Wilken CS/2 Finanzmanagement - Version: 4401-000",
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetColumnSpan(version, 2); Grid.SetRow(version, 7); root.Children.Add(version);

        Content = root;
    }
}
