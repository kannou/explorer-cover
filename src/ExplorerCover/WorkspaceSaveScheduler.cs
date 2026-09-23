using System.IO;
using System.Windows.Threading;

namespace ExplorerCover;

// UIスレッド専用。保存準備も変更時だけ行い、失敗と保存中の変更は未保存のまま残す。
internal sealed class WorkspaceSaveScheduler : IDisposable
{
    private readonly Func<string> capture;
    private readonly Func<string, Task> save;
    private readonly Action<string?> report;
    private readonly DispatcherTimer timer;
    private Task<string?> saving = Task.FromResult<string?>(null);
    private string? lastSaved;
    private long revision;
    private long savedRevision = -1; // 初回は変更通知がなくても保存する。
    private bool isSaving;
    private bool paused;
    private bool disposed;

    public WorkspaceSaveScheduler(Func<string> capture, Func<string, Task> save, Action<string?> report, TimeSpan? interval = null)
    {
        this.capture = capture; this.save = save; this.report = report;
        timer = new() { Interval = interval ?? TimeSpan.FromSeconds(1) };
        timer.Tick += Tick;
        Schedule();
    }

    public void RequestSave()
    {
        if (disposed) return;
        revision++;
        Schedule();
    }

    private void Schedule()
    {
        timer.Stop();
        if (!disposed && !paused && !isSaving && revision != savedRevision) timer.Start();
    }

    private async void Tick(object? sender, EventArgs e)
    {
        timer.Stop();
        if (disposed || paused || isSaving || revision == savedRevision) return;
        var error = await BeginSave();
        if (disposed) return;
        report(error);
        Schedule();
    }

    private Task<string?> BeginSave()
    {
        isSaving = true;
        saving = SaveAsync();
        return saving;
    }

    private async Task<string?> SaveAsync()
    {
        var capturedRevision = revision;
        try
        {
            var json = capture();
            if (json != lastSaved) await save(json);
            lastSaved = json;
            savedRevision = capturedRevision;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or InvalidOperationException)
        {
            DiagnosticLog.Write("Workspace save failed: " + ex.Message);
            return "作業状態を保存できません: " + ex.Message;
        }
        finally { isSaving = false; }
    }

    // 終了時は通常の待機を止め、ウィンドウ配置も取り直して最後の変更まで保存する。
    public async Task<string?> FlushAsync()
    {
        paused = true; timer.Stop();
        // 変更通知のない最終配置の取得に失敗しても、終了取消後に再試行できる。
        revision++;
        await saving;
        if (disposed) return null;
        string? error;
        do { error = await BeginSave(); }
        while (error == null && !disposed && revision != savedRevision);
        return error;
    }

    public void Resume() { paused = false; Schedule(); }
    public void Dispose() { disposed = true; timer.Stop(); timer.Tick -= Tick; }
}
