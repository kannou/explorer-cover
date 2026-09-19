using System.Windows;

namespace ExplorerAlt;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DiagnosticLog.Write("Startup");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var window = new MainWindow(e.Args.ElementAtOrDefault(0) ?? home, e.Args.ElementAtOrDefault(1) ?? home);
        MainWindow = window;
        window.Show();
        DiagnosticLog.Write("Window shown");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.Write($"Exit: {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
