using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerCover;
using ExplorerCover.Core;

// ウィンドウ・Shell処理を起動せず、実際の行と遅延させたアイコン取得で検証する。
internal static class Program
{
    private static int count, failures;
    private static readonly Task<ImageSource?> NoIcon = Task.FromResult<ImageSource?>(null);
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
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static WorkspaceState State() => new(@"C:\left", @"C:\right");
    private static SidebarView View(WorkspaceState state, Func<string?, Task<ImageSource?>>? icons = null, Action<string>? navigate = null, bool initialize = false) =>
        new(state, navigate ?? (_ => { }), initialize, null, null, icons ?? (_ => NoIcon));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static Button[] Rows(SidebarView view) => Descendants(view).OfType<Button>()
        .Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Sidebar.Bookmark.")).ToArray();
    private static Image Icon(Button row) => ((Grid)row.Content).Children.OfType<Image>().Single();
    private static async Task Drain() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(5);
        }
    }
    private static TaskCompletionSource<ImageSource?> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Run()
    {
        await Check("初期登録は一度だけ描画し、空の復元状態は空のままにする", async () =>
        {
            var state = State(); var calls = new List<string?>();
            using var view = View(state, path => { calls.Add(path); return NoIcon; }, initialize: true);
            await Drain();
            Assert(Rows(view).Length == state.Sidebar.Bookmarks.Count && calls.Count == state.Sidebar.Bookmarks.Count * 2, "初期行を繰り返し生成した");
            using var empty = View(State()); Assert(Rows(empty).Length == 0, "空の一覧に初期項目を追加した");
        });
        await Check("既存1000行の追加・削除では変更した行だけを生成し、アイコンを再要求しない", async () =>
        {
            var state = State();
            for (var i = 0; i < 1000; i++) state.Sidebar.Add($"項目{i}", $@"C:\Folder-{i}");
            var calls = 0;
            using var view = View(state, _ => { calls++; return NoIcon; });
            var original = Rows(view); calls = 0;
            var added = state.Sidebar.Add("追加", @"C:\new");
            var afterAdd = Rows(view);
            Assert(afterAdd.Length == 1001 && original.SequenceEqual(afterAdd.Take(1000)), "既存行を作り直した");
            Assert(calls == 2, "新しい行以外のアイコンを要求した");
            state.Sidebar.Add("重複", "c:/new/"); Assert(calls == 2 && Rows(view).Length == 1001, "重複登録で描画した");
            state.Sidebar.Remove(state.Sidebar.Bookmarks[500]);
            var expected = original.Take(500).Concat(original.Skip(501)).Append(afterAdd[^1]);
            Assert(expected.SequenceEqual(Rows(view)) && calls == 2, "削除以外の行を作り直した");
            state.Sidebar.Remove(added);
            Assert(Rows(view).Length == 999 && calls == 2, "追加後の削除でアイコンを要求した");
            await Drain();
            Console.WriteLine("計測: 既存1000行への1件追加は新規1行・アイコン要求2回（共通+対象）、削除時はともに0");
        });
        await Check("差分更新後も改名・表示パス・移動・削除メニューを同じ行で利用できる", async () =>
        {
            var state = State(); var first = state.Sidebar.Add("最初", @"C:\first");
            string? target = null;
            using var view = View(state, navigate: path => target = path);
            var row = Rows(view).Single(); var menu = row.ContextMenu;
            state.Sidebar.Add("追加", @"C:\second"); first.Rename("変更後"); await Drain();
            Assert(ReferenceEquals(row, Rows(view)[0]) && ReferenceEquals(menu, row.ContextMenu), "改名で行を置換した");
            Assert(AutomationProperties.GetName(row) == "変更後" && AutomationProperties.GetHelpText(row) == @"変更後 (C:\first)" && Equals(row.ToolTip, first.DisplayText), "表示や支援情報を更新できない");
            var label = ((Grid)row.Content).Children.OfType<TextBlock>().Single();
            Assert(((Run)label.Inlines.FirstInline!).Text == "変更後", "行内の名前を更新できない");
            row.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Assert(target == first.Path, "移動先が変わった");
            ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(state.Sidebar.Bookmarks.Count == 1 && Rows(view).Length == 1, "削除メニューが古い登録を参照した");
        });
        await Check("全件削除と再登録を繰り返しても順序と行数を維持する", async () =>
        {
            var state = State(); using var view = View(state);
            for (var round = 0; round < 3; round++)
            {
                var first = state.Sidebar.Add("先頭", @"C:\a"); var second = state.Sidebar.Add("末尾", @"C:\b");
                state.Sidebar.Remove(first); var replacement = state.Sidebar.Add("再登録", @"C:\a");
                Assert(Rows(view).Select(AutomationProperties.GetName).SequenceEqual(["末尾", "再登録"]), "再登録の表示順が不正");
                state.Sidebar.Remove(second); state.Sidebar.Remove(replacement);
                Assert(Rows(view).Length == 0, "削除済みの行が残った");
            }
            await Drain();
        });
        await Check("共通アイコン待機中の削除は対象パスを要求せず、遅い結果も反映しない", async () =>
        {
            var state = State(); var first = state.Sidebar.Add("遅い", @"C:\slow");
            var pending = Pending(); var calls = 0;
            using var view = View(state, _ => { calls++; return pending.Task; });
            var icon = Icon(Rows(view).Single()); state.Sidebar.Remove(first); await Drain();
            pending.SetResult(new DrawingImage()); await Drain();
            Assert(calls == 1 && icon.Source == null, "削除後に取得を続けた");
        });
        await Check("対象アイコン待機中の削除は共有取得を中止せず、残った行だけ更新する", async () =>
        {
            var state = State(); var first = state.Sidebar.Add("削除", @"C:\first"); state.Sidebar.Add("残す", @"C:\second");
            var pending = Pending(); var fallback = new DrawingImage(); var actual = new DrawingImage();
            var calls = 0;
            using var view = View(state, path => { calls++; return path == null ? Task.FromResult<ImageSource?>(fallback) : pending.Task; });
            var before = Rows(view); var removedIcon = Icon(before[0]); var keptIcon = Icon(before[1]);
            state.Sidebar.Remove(first); await Drain();
            Assert(!pending.Task.IsCompleted, "共有取得をキャンセルした");
            pending.SetResult(actual); await Until(() => ReferenceEquals(keptIcon.Source, actual));
            Assert(ReferenceEquals(removedIcon.Source, fallback) && ReferenceEquals(keptIcon.Source, actual) && calls == 4, "削除済み行を更新したか、残った行の取得をやり直した");
        });
        await Check("キャンセル済み・完了直後のアイコン待機も表示せずに終了する", async () =>
        {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            var image = new Image(); var calls = 0;
            await SidebarIconLoader.LoadAsync(image, "before", _ => { calls++; return NoIcon; }, cancel.Token);
            Assert(calls == 0, "キャンセル済みでも取得を開始した");
            using var race = new CancellationTokenSource();
            await SidebarIconLoader.LoadAsync(image, "race", _ => { race.Cancel(); return Task.FromResult<ImageSource?>(new DrawingImage()); }, race.Token);
            Assert(image.Source == null, "取得完了とキャンセルが重なると結果を表示した");
        });
        await Check("アイコン未取得時は共通画像を保持する", async () =>
        {
            var fallback = new DrawingImage(); var image = new Image();
            await SidebarIconLoader.LoadAsync(image, "missing", path => path == null ? Task.FromResult<ImageSource?>(fallback) : NoIcon, CancellationToken.None);
            Assert(ReferenceEquals(fallback, image.Source), "取得失敗時に共通画像を消した");
        });
        await Check("破棄時は全行の待機と変更購読を解除する", async () =>
        {
            var state = State(); state.Sidebar.Add("遅い", @"C:\slow");
            var pending = Pending(); var calls = 0;
            using var view = View(state, _ => { calls++; return pending.Task; });
            var icon = Icon(Rows(view).Single()); view.Dispose(); view.Dispose();
            state.Sidebar.Add("終了後", @"C:\after"); await Drain();
            pending.SetResult(new DrawingImage()); await Drain();
            Assert(Rows(view).Length == 0 && calls == 1 && icon.Source == null, "破棄後に行を生成・更新した");
        });
        await Check("共有取得が終わらなくても、削除した行をGCで回収できる", async () =>
        {
            var state = State(); var pending = Pending();
            using var view = View(state, path => path == null ? NoIcon : pending.Task);
            var references = AddAndRemove(view, state);
            await Drain(); await Drain();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Drain();
            }
            Assert(!pending.Task.IsCompleted && references.All(r => !r.IsAlive), "未完了の共有取得が削除済み行を保持している");
            GC.KeepAlive(pending); GC.KeepAlive(view);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] AddAndRemove(SidebarView view, WorkspaceState state)
    {
        var references = new List<WeakReference>();
        for (var i = 0; i < 20; i++)
        {
            var bookmark = state.Sidebar.Add("遅い", $@"C:\slow-{i}");
            var row = Rows(view).Single();
            references.Add(new(row)); references.Add(new(Icon(row)));
            state.Sidebar.Remove(bookmark);
        }
        return references.ToArray();
    }
}
