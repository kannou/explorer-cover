using System.Windows;
using ExplorerCover.Commands;
using ExplorerCover.Core;
using System.IO;

namespace ExplorerCover;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DiagnosticLog.Write("Startup");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var settings = ShortcutSettings.Load();
        var mouse = MouseSettingsFile.Load();
        var quickLook = QuickLookSettingsFile.Load();
        var customState = Environment.GetEnvironmentVariable("EXPLORER_COVER_STATE");
        WorkspaceStore? store = null;
        WorkspaceSnapshot? snapshot = null;
        string? stateWarning = null;
        if (e.Args.Length == 0 || customState != null)
        {
            store = new WorkspaceStore(customState ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "explorer_cover", "workspace.json"));
            (snapshot, stateWarning) = await Task.Run(store.Load);
            if (e.Args.Length != 0) snapshot = null;
        }
        var warning = string.Join("\n", new[] { settings.Warning, mouse.Warning, quickLook.Warning, stateWarning }.Where(value => value != null));
        var window = new MainWindow(e.Args.ElementAtOrDefault(0) ?? home, e.Args.ElementAtOrDefault(1) ?? home, settings.Service, warning.Length == 0 ? null : warning, mouse.Settings, quickLook.Settings, snapshot?.Restore());
        WindowLayout.RestoreOnShow(window, snapshot?.Window);
        if (store != null) _ = new WorkspacePersistence(window, store);
        else window.Title += "（一時セッション）";
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
