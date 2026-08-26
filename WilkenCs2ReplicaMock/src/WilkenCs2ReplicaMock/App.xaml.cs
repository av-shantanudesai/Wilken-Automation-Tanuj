using System.Linq;
using System.Windows;

namespace WilkenCs2ReplicaMock;

public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        // WPF StartupEventArgs.Args is often empty when launched from another process.
        // Also read Environment.GetCommandLineArgs and WILKEN_REPLICA_MANUAL_LOGIN.
        if (WantsManualLogin(e.Args))
        {
            new EhpStartupWindow().Show();
            return;
        }

        new MainWindow().Show();
    }

    private static bool WantsManualLogin(string[] startupArgs)
    {
        var env = Environment.GetEnvironmentVariable("WILKEN_REPLICA_MANUAL_LOGIN");
        if (env is "1" or "true" or "TRUE" or "yes" or "YES")
            return true;

        return (startupArgs ?? Array.Empty<string>())
            .Concat(Environment.GetCommandLineArgs())
            .Any(a => a.Equals("--manual-login", StringComparison.OrdinalIgnoreCase));
    }
}
