using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ExplorerCover.Core;

namespace ExplorerCover;

internal sealed class WorkspacePersistence
{
    private readonly MainWindow window;
    private readonly WorkspaceStore store;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Task<string?> saving = Task.FromResult<string?>(null);
    private string? lastSaved;
    private bool closing;
    private bool mayClose;
    private bool maximized;
    public WorkspacePersistence(MainWindow window, WorkspaceStore store)
    {
        this.window = window; this.store = store;
        maximized = window.WindowState == WindowState.Maximized;
        window.StateChanged += (_, _) => { if (window.WindowState != WindowState.Minimized) maximized = window.WindowState == WindowState.Maximized; };
        timer.Tick += Tick;
        if (store.CanSave) timer.Start();
        window.Closing += Closing;
        window.Closed += (_, _) => { timer.Stop(); timer.Tick -= Tick; store.Dispose(); };
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (closing || !saving.IsCompleted) return;
        saving = SaveAsync();
        window.ShowSaveWarning(await saving);
    }
    private async Task<string?> SaveAsync()
    {
        try
        {
            var snapshot = WorkspaceSnapshot.Capture(window.State) with { Window = WindowLayout.Capture(window, maximized) };
            var json = snapshot.ToJson();
            if (json == lastSaved) return null;
            await Task.Run(() => store.Save(json));
            lastSaved = json; return null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or FormatException or NotSupportedException or InvalidOperationException)
        { DiagnosticLog.Write("Workspace save failed: " + ex.Message); return "作業状態を保存できません: " + ex.Message; }
    }
    private async void Closing(object? sender, CancelEventArgs e)
    {
        if (mayClose || !store.CanSave) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; timer.Stop(); window.IsEnabled = false;
        await saving;
        var error = await SaveAsync();
        window.IsEnabled = true;
        if (error != null && MessageBox.Show(window, error + "\n保存せずに終了しますか？", "作業状態の保存", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            closing = false; window.ShowSaveWarning(error); timer.Start(); return;
        }
        mayClose = true;
        _ = window.Dispatcher.BeginInvoke(() => window.Close());
    }
}
