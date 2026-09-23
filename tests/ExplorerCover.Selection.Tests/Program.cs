using System.Windows.Threading;
using ExplorerCover;

// ウィンドウを作らずDispatcherだけを動かし、通知・通信待機の競合を検証する。
internal static class Program
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);
    private static int count, failures;

    [STAThread]
    private static int Main()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(async () =>
        {
            try { await Run(); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        Console.WriteLine($"{count - failures}/{count} passed");
        return failures == 0 ? 0 : 1;
    }

    private static async Task Check(string name, Func<Task> action)
    {
        count++;
        try { await action(); Console.WriteLine("PASS: " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL: " + name + ": " + ex); }
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(5);
        }
    }

    private static async Task Run()
    {
        await Check("無操作時は確認せず、連続通知を一回にまとめて再び停止する", async () =>
        {
            var calls = 0;
            using var scheduler = new SelectionRefreshScheduler(() => true, () => { calls++; return Task.CompletedTask; }, Interval);
            await Task.Delay(100); Assert(calls == 0, "起動時から確認している");
            for (var i = 0; i < 100; i++) scheduler.Request();
            await Until(() => calls == 1);
            await Task.Delay(120); Assert(calls == 1, "通知なしで確認が続いている");
        });

        await Check("プレビュー未使用・非アクティブ時は確認せず、復帰通知で最新を確認する", async () =>
        {
            var allowed = false; var calls = 0;
            using var scheduler = new SelectionRefreshScheduler(() => allowed, () => { calls++; return Task.CompletedTask; }, Interval);
            scheduler.Request(); await Task.Delay(100);
            Assert(calls == 0, "停止条件中に確認した");
            allowed = true; scheduler.Request(); await Until(() => calls == 1);
            scheduler.Request(); allowed = false; await Task.Delay(100);
            Assert(calls == 1, "予約後の停止を無視した");
            allowed = true; scheduler.Request(); await Until(() => calls == 2);
        });

        await Check("送信待機中の変更は並列実行せず、完了後に最後の選択だけを取得する", async () =>
        {
            var selected = "A"; var sent = new List<string>();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var scheduler = new SelectionRefreshScheduler(() => true, async () =>
            {
                sent.Add(selected);
                if (sent.Count == 1) await release.Task;
            }, Interval);
            scheduler.Request(); await Until(() => sent.Count == 1);
            selected = "B"; scheduler.Request(); selected = "C"; scheduler.Request();
            await Task.Delay(100);
            Assert(sent.SequenceEqual(["A"]) && !scheduler.Pending.IsCompleted, "送信中に次の確認を開始した");
            release.SetResult(); await Until(() => sent.Count == 2);
            Assert(sent.SequenceEqual(["A", "C"]), "最後の選択を取りこぼした");
            await Task.Delay(100); Assert(sent.Count == 2, "完了後も確認が続いた");
        });

        await Check("Toggle待機中はSwitchを追加せず、Toggle完了通知後に再評価する", async () =>
        {
            var togglePending = false; var calls = 0;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var scheduler = new SelectionRefreshScheduler(() => !togglePending, async () =>
            {
                calls++;
                if (calls == 1) await release.Task;
            }, Interval);
            scheduler.Request(); await Until(() => calls == 1);
            togglePending = true; scheduler.Request();
            var pending = scheduler.Pending;
            Assert(!pending.IsCompleted, "送信完了前にToggleへ進める");
            release.SetResult(); await pending; await Task.Delay(100);
            Assert(calls == 1, "Toggle待機中にSwitchを開始した");
            togglePending = false; scheduler.Request(); await Until(() => calls == 2);
        });

        await Check("確認中に同期的に再入した通知も一回だけ追従する", async () =>
        {
            var calls = 0;
            SelectionRefreshScheduler scheduler = null!;
            using (scheduler = new(() => true, () =>
            {
                calls++;
                if (calls == 1) scheduler.Request();
                return Task.CompletedTask;
            }, Interval))
            {
                scheduler.Request(); await Until(() => calls == 2);
                await Task.Delay(100); Assert(calls == 2, "再入通知の処理数が不正");
            }
        });

        await Check("破棄後は予約済み・新規の通知を処理しない", async () =>
        {
            var calls = 0;
            var scheduler = new SelectionRefreshScheduler(() => true, () => { calls++; return Task.CompletedTask; }, Interval);
            scheduler.Request(); scheduler.Dispose(); scheduler.Request();
            await Task.Delay(100); Assert(calls == 0, "終了後に確認した");
        });

        await Check("送信中に破棄しても完了待機を解放し、次の確認は行わない", async () =>
        {
            var calls = 0;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var scheduler = new SelectionRefreshScheduler(() => true, async () => { calls++; await release.Task; }, Interval);
            scheduler.Request(); await Until(() => calls == 1);
            var pending = scheduler.Pending;
            scheduler.Request(); scheduler.Dispose(); release.SetResult();
            await pending; await Task.Delay(100);
            Assert(calls == 1, "終了後に追従処理が始まった");
        });
    }
}
