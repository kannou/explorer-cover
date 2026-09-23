using System.IO;
using System.Windows.Threading;
using ExplorerCover;
using ExplorerCover.Core;

// ウィンドウを開かず、保存準備の回数と非同期書き込み中の変更・終了を検証する。
internal static class Program
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(30);
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
        await Check("初回保存後はJSON生成を止め、入力途中を除外して変更通知をまとめる", async () =>
        {
            var state = new WorkspaceState(@"C:\left", @"C:\right");
            using var changes = new WorkspaceChangeTracker(state);
            var captures = 0; var written = new List<WorkspaceSnapshot>();
            using var saves = new WorkspaceSaveScheduler(
                () => { captures++; return WorkspaceSnapshot.Capture(state).ToJson(); },
                json => { written.Add(WorkspaceSnapshot.FromJson(json)); return Task.CompletedTask; }, _ => { }, Interval);
            changes.Changed += saves.RequestSave;
            await Until(() => written.Count == 1);
            state.Left.SelectedTab.AddressText = "入力途中";
            state.Left.SelectedTab.NavigationFailed("存在しません");
            await Task.Delay(150);
            Assert(captures == 1, "未変更時や編集中にJSONを生成した");
            state.Left.SelectedTab.NavigationSucceeded(@"C:\child");
            state.Activate(state.Right); state.Sidebar.Width = 300;
            state.Sidebar.Add("登録", @"C:\saved").Rename("変更後");
            await Until(() => written.Count == 2);
            Assert(captures == 2 && written[1].Left.Paths[0] == @"C:\child" && written[1].ActivePane == "right" &&
                written[1].SidebarWidth == 300 && written[1].Bookmarks[0].Name == "変更後", "最後の状態を一回で保存できなかった");
            await Task.Delay(150); Assert(captures == 2, "保存後もJSON生成が続いた");
        });

        await Check("通知があっても最終JSONが同じなら書き込まない", async () =>
        {
            var captures = 0; var writes = 0;
            using var saves = new WorkspaceSaveScheduler(() => { captures++; return "A"; },
                _ => { writes++; return Task.CompletedTask; }, _ => { }, Interval);
            await Until(() => writes == 1);
            saves.RequestSave(); await Until(() => captures == 2);
            await Task.Delay(120); Assert(writes == 1 && captures == 2, "同じ状態を再保存した");
        });

        await Check("保存中の変更を失わず、書き込みを直列化して最新状態を追加保存する", async () =>
        {
            var current = "A"; var captures = 0; var written = new List<string>();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var saves = new WorkspaceSaveScheduler(() => { captures++; return current; }, async json =>
            {
                written.Add(json);
                if (written.Count == 1) await release.Task;
            }, _ => { }, Interval);
            await Until(() => written.Count == 1);
            current = "B"; saves.RequestSave(); current = "C"; saves.RequestSave();
            await Task.Delay(120); Assert(captures == 1, "書き込み中に次の保存を開始した");
            release.SetResult(); await Until(() => written.Count == 2);
            Assert(written.SequenceEqual(["A", "C"]), "最新の変更を失った");
        });

        await Check("保存失敗は通知して自動再試行し、復旧後は通知を消して停止する", async () =>
        {
            var captures = 0; var attempts = 0; var fail = true; var reports = new List<string?>();
            using var saves = new WorkspaceSaveScheduler(() => { captures++; return "A"; }, _ =>
            {
                attempts++;
                if (fail) throw new IOException("保存先が使用中です");
                return Task.CompletedTask;
            }, reports.Add, Interval);
            await Until(() => reports.Count > 0);
            Assert(reports[0]?.Contains("保存先が使用中") == true, "エラーを通知していない");
            fail = false; await Until(() => reports[^1] == null);
            var countAfterRecovery = captures;
            await Task.Delay(120);
            Assert(attempts >= 2 && captures == countAfterRecovery, "復旧後も保存が続いた");
        });

        await Check("JSON生成の失敗も未保存として扱い、次回に再試行する", async () =>
        {
            var invalid = true; var writes = 0; var reports = new List<string?>();
            using var saves = new WorkspaceSaveScheduler(() => invalid ? throw new FormatException("状態が不正です") : "A",
                _ => { writes++; return Task.CompletedTask; }, reports.Add, Interval);
            await Until(() => reports.Count > 0); Assert(writes == 0 && reports[0] != null, "不正な状態を保存した");
            invalid = false; await Until(() => writes == 1);
        });

        await Check("終了時は予約を待たず配置を取り直し、最終保存中の変更も保存する", async () =>
        {
            var current = "初期状態"; var written = new List<string>();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var saves = new WorkspaceSaveScheduler(() => current, async json =>
            {
                written.Add(json);
                if (written.Count == 2) await release.Task;
            }, _ => { }, Interval);
            await Until(() => written.Count == 1);
            current = "終了直前の配置"; // 通知がなくても終了時には取得する。
            var flush = saves.FlushAsync();
            Assert(written.Count == 2 && !flush.IsCompleted, "最終保存を待たずに終了した");
            current = "移動完了後の状態"; saves.RequestSave();
            release.SetResult();
            Assert(await flush == null && written.SequenceEqual(["初期状態", "終了直前の配置", "移動完了後の状態"]), "最終保存中の変更を失った");
            await Task.Delay(120); Assert(written.Count == 3, "終了待機中にタイマーが再開した");
        });

        await Check("終了時は実行中の自動保存を待ち、予約された最新状態まで保存する", async () =>
        {
            var current = "A"; var written = new List<string>();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var saves = new WorkspaceSaveScheduler(() => current, async json =>
            {
                written.Add(json);
                if (written.Count == 1) await release.Task;
            }, _ => { }, Interval);
            await Until(() => written.Count == 1);
            current = "B"; saves.RequestSave(); var flush = saves.FlushAsync();
            await Task.Delay(120); Assert(written.Count == 1 && !flush.IsCompleted, "実行中の保存を待っていない");
            release.SetResult(); Assert(await flush == null && written.SequenceEqual(["A", "B"]), "終了直前の変更を保存していない");
        });

        await Check("保存失敗による終了取消後に自動保存を再開できる", async () =>
        {
            var fail = true; var writes = 0;
            using var saves = new WorkspaceSaveScheduler(() => "A", _ =>
            {
                if (fail) throw new IOException("保存できません");
                writes++; return Task.CompletedTask;
            }, _ => { }, Interval);
            Assert(await saves.FlushAsync() != null, "終了時の失敗を返していない");
            fail = false; saves.Resume(); await Until(() => writes == 1);
        });

        await Check("保存済み状態からの終了時再取得が失敗しても、取消後に再試行する", async () =>
        {
            var current = "A"; var fail = false; var written = new List<string>();
            using var saves = new WorkspaceSaveScheduler(() => current, json =>
            {
                if (fail) throw new IOException("保存できません");
                written.Add(json); return Task.CompletedTask;
            }, _ => { }, Interval);
            await Until(() => written.Count == 1);
            current = "B"; fail = true;
            Assert(await saves.FlushAsync() != null, "終了時の失敗を返していない");
            fail = false; saves.Resume(); await Until(() => written.Count == 2);
            Assert(written.SequenceEqual(["A", "B"]), "変更通知のない最終配置を再保存しなかった");
        });

        await Check("破棄時は予約と保存完了後の再実行を止める", async () =>
        {
            var captures = 0;
            var pending = new WorkspaceSaveScheduler(() => { captures++; return "A"; }, _ => Task.CompletedTask, _ => { }, Interval);
            pending.Dispose(); pending.RequestSave(); pending.Resume();
            await Task.Delay(100); Assert(captures == 0, "破棄後に予約を処理した");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reports = 0;
            using var active = new WorkspaceSaveScheduler(() => { captures++; return "B"; }, _ => release.Task, _ => reports++, Interval);
            await Until(() => captures == 1);
            active.RequestSave(); active.Dispose(); release.SetResult();
            await Task.Delay(120); Assert(captures == 1 && reports == 0, "終了後に表示や保存を再開した");
        });
    }
}
