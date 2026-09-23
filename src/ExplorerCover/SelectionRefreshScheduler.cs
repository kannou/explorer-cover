using System.Windows.Threading;

namespace ExplorerCover;

// UIスレッド専用。通知を一回の確認にまとめ、処理中の通知は完了後に再評価する。
internal sealed class SelectionRefreshScheduler : IDisposable
{
    private readonly DispatcherTimer timer;
    private readonly Func<bool> canRefresh;
    private readonly Func<Task> refresh;
    private bool requested;
    private bool disposed;
    public Task Pending { get; private set; } = Task.CompletedTask;

    public SelectionRefreshScheduler(Func<bool> canRefresh, Func<Task> refresh, TimeSpan? interval = null)
    {
        this.canRefresh = canRefresh; this.refresh = refresh;
        timer = new() { Interval = interval ?? TimeSpan.FromMilliseconds(200) };
        timer.Tick += Tick;
    }

    public void Request()
    {
        if (disposed) return;
        requested = true;
        Schedule();
    }

    private void Schedule()
    {
        if (!disposed && requested && Pending.IsCompleted && canRefresh() && !timer.IsEnabled)
            timer.Start();
    }

    private async void Tick(object? sender, EventArgs e)
    {
        timer.Stop();
        if (disposed || !requested || !Pending.IsCompleted || !canRefresh()) return;
        requested = false;
        // refresh内のCOM呼び出しが再入しても、次の確認を並列実行しない。
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending = completion.Task;
        try { await refresh(); }
        finally
        {
            completion.SetResult();
            Schedule();
        }
    }

    public void Dispose()
    {
        disposed = true; requested = false;
        timer.Stop(); timer.Tick -= Tick;
    }
}
