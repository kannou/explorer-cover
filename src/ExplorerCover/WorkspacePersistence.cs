using System.ComponentModel;
using System.Windows;
using ExplorerCover.Core;

namespace ExplorerCover;

internal sealed class WorkspacePersistence
{
    private readonly MainWindow window;
    private readonly WorkspaceStore store;
    private readonly WorkspaceChangeTracker? changes;
    private readonly WorkspaceSaveScheduler? saves;
    private bool closing;
    private bool mayClose;
    private bool maximized;
    public WorkspacePersistence(MainWindow window, WorkspaceStore store)
    {
        this.window = window; this.store = store;
        maximized = window.WindowState == WindowState.Maximized;
        if (store.CanSave)
        {
            saves = new(CaptureJson, json => Task.Run(() => store.Save(json)), window.ShowSaveWarning);
            changes = new(window.State);
            changes.Changed += saves.RequestSave;
            window.LocationChanged += WindowChanged;
            window.SizeChanged += WindowSizeChanged;
            window.StateChanged += WindowStateChanged;
        }
        window.Closing += Closing;
        window.Closed += Closed;
    }
    private void WindowChanged(object? sender, EventArgs e) => saves?.RequestSave();
    private void WindowSizeChanged(object sender, SizeChangedEventArgs e) => saves?.RequestSave();
    private void WindowStateChanged(object? sender, EventArgs e)
    {
        if (window.WindowState != WindowState.Minimized) maximized = window.WindowState == WindowState.Maximized;
        saves?.RequestSave();
    }
    private string CaptureJson()
    {
        DiagnosticLog.Write("Workspace snapshot captured");
        return (WorkspaceSnapshot.Capture(window.State) with { Window = WindowLayout.Capture(window, maximized) }).ToJson();
    }
    private async void Closing(object? sender, CancelEventArgs e)
    {
        if (mayClose || saves == null) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; window.IsEnabled = false;
        var error = await saves.FlushAsync();
        window.IsEnabled = true;
        if (error != null && MessageBox.Show(window, error + "\n保存せずに終了しますか？", "作業状態の保存", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            closing = false; window.ShowSaveWarning(error); saves.Resume(); return;
        }
        mayClose = true;
        _ = window.Dispatcher.BeginInvoke(() => window.Close());
    }
    private void Closed(object? sender, EventArgs e)
    {
        saves?.Dispose(); changes?.Dispose();
        if (changes != null && saves != null) changes.Changed -= saves.RequestSave;
        window.LocationChanged -= WindowChanged;
        window.SizeChanged -= WindowSizeChanged;
        window.StateChanged -= WindowStateChanged;
        window.Closing -= Closing; window.Closed -= Closed;
        store.Dispose();
    }
}
