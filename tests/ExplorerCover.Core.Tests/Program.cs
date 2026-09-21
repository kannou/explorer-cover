using ExplorerCover.Core;

var failures = new List<string>();
var count = 0;
void Check(string name, Action test)
{
    count++;
    try { test(); Console.WriteLine($"PASS: {name}"); }
    catch (Exception ex) { failures.Add(name); Console.WriteLine($"FAIL: {name}: {ex.Message}"); }
}
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"expected={expected}, actual={actual}"); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"{typeof(T).Name}が発生しませんでした。");
}
ShortcutGesture Key(string gesture) => ShortcutGesture.Parse(gesture);
string Json(string bindings) => "{\"version\":1,\"bindings\":" + bindings + "}";

Check("失敗した移動は現在地と履歴を変更しない", () =>
{
    var tab = new TabState(@"C:\A");
    Equal<string?>(null, tab.CurrentPath);
    Equal(0, tab.History.Entries.Count);
    tab.NavigationSucceeded(@"C:\A");
    tab.AddressText = @"C:\missing";
    tab.NavigationFailed("存在しません");
    Equal(@"C:\A", tab.CurrentPath);
    Equal(1, tab.History.Entries.Count);
    tab.CancelAddressEdit();
    Equal(@"C:\A", tab.AddressText);
    tab.NavigationSucceeded(@"C:\B");
    Equal<string?>(null, tab.Error);
});
Check("戻る後の新しい移動は進む履歴を破棄する", () =>
{
    var h = new NavigationHistory();
    h.Commit(@"C:\A"); h.Commit(@"C:\B"); h.Commit(@"C:\C");
    h.Commit(@"C:\B", 1);
    Equal(true, h.CanGoForward);
    h.Commit(@"C:\D");
    Equal(false, h.CanGoForward);
    Equal(@"C:\D", h.Current);
    Equal(3, h.Entries.Count);
    Equal(@"C:\A", h.Entries[0]);
});
Check("履歴への不正な確定は状態を変更しない", () =>
{
    var h = new NavigationHistory(); h.Commit(@"C:\A"); h.Commit(@"C:\B");
    Throws<ArgumentException>(() => h.Commit(@"C:\unexpected", 0));
    Equal(1, h.Index);
    Equal(@"C:\B", h.Current);
    h.Commit(@"c:\b\");
    Equal(2, h.Entries.Count);
});
Check("WSLの大小文字は履歴とブックマークで区別し、サーバー別名は同一視する", () =>
{
    var upper = @"\\wsl.localhost\Ubuntu-24.04\home\user\Case";
    var lower = @"\\wsl.localhost\Ubuntu-24.04\home\user\case";
    Equal(false, NavigationHistory.SameLocation(upper, lower));
    Equal(true, NavigationHistory.SameLocation(upper, @"\\WSL$\ubuntu-24.04\home\user\Case\"));
    Equal(true, NavigationHistory.SameLocation(upper, @"\\?\UNC\wsl.localhost\Ubuntu-24.04\home\user\Case"));
    Equal(false, NavigationHistory.SameLocation(upper, @"\\other\Ubuntu-24.04\home\user\Case"));
    var tab = new TabState(upper); var navigation = new TabNavigation(tab);
    navigation.Complete(upper); navigation.Complete(lower);
    Equal(2, tab.History.Entries.Count);
    navigation.BeginHistory(-1); navigation.Complete(upper.Replace("wsl.localhost", "wsl$"));
    Equal(0, tab.History.Index); Equal(true, tab.History.CanGoForward);
    var sidebar = new SidebarState(); sidebar.Add("Case", upper); sidebar.Add("case", lower);
    sidebar.Add("alias", upper.Replace("wsl.localhost", "wsl$"));
    Equal(2, sidebar.Bookmarks.Count);
});
Check("履歴移動の確認中に通常移動へ変更した場合は新しい履歴になる", () =>
{
    var tab = new TabState(@"C:\a"); var navigation = new TabNavigation(tab);
    navigation.Complete(@"C:\a"); navigation.Complete(@"C:\b");
    navigation.BeginHistory(-1); navigation.BeginNavigation(); navigation.Complete(@"C:\a");
    Equal(3, tab.History.Entries.Count); Equal(2, tab.History.Index);
});
Check("タブとペインの状態は独立し最後のタブを閉じない", () =>
{
    var state = new WorkspaceState(@"C:\left", @"C:\right");
    var first = state.Left.SelectedTab;
    var second = state.Left.AddTab(@"C:\second");
    first.NavigationSucceeded(@"C:\visited");
    state.Left.SelectTab(second);
    Equal(0, second.History.Entries.Count);
    Equal(0, state.Right.SelectedTab.History.Entries.Count);
    Throws<ArgumentException>(() => state.Left.SelectTab(state.Right.SelectedTab));
    Equal(true, state.Left.CloseTab(second));
    Equal(first, state.Left.SelectedTab);
    Equal(false, state.Left.CloseTab(first));
    state.Activate(state.Right);
    Equal(state.Right, state.ActivePane);
    Throws<ArgumentException>(() => state.Activate(new PaneState("other")));
    Throws<ArgumentOutOfRangeException>(() => state.LeftPaneRatio = double.NaN);
});
Check("コマンドは明示した対象で一度実行しCanExecuteを尊重する", () =>
{
    var state = new WorkspaceState("left", "right");
    var dispatcher = new CommandDispatcher();
    PaneState? target = null; int executed = 0;
    dispatcher.Register(CommandIds.NavigateAddress, p => { target = p; executed++; }, p => p.SelectedTab.AddressText.Length > 0);
    Equal(true, dispatcher.Execute(CommandIds.NavigateAddress, state.Right));
    Equal(state.Right, target); Equal(1, executed);
    state.Right.SelectedTab.AddressText = "";
    Equal(false, dispatcher.Execute(CommandIds.NavigateAddress, state.Right)); Equal(1, executed);
});
Check("初期キーと入力範囲", () =>
{
    var map = new ShortcutMap();
    Equal(CommandIds.FocusAddress, map.Resolve(Key("Ctrl+L"), InputScope.Browser));
    Equal(CommandIds.NavigateAddress, map.Resolve(Key("Enter"), InputScope.Address));
    Equal<string?>(null, map.Resolve(Key("Enter"), InputScope.Browser));
    Equal<string?>(null, map.Resolve(Key("Ctrl+L"), InputScope.None));
    Equal(CommandIds.Copy, map.Resolve(Key("Ctrl+C"), InputScope.Browser));
    Equal<string?>(null, map.Resolve(Key("Ctrl+C"), InputScope.Address));
});
Check("キー変更・解除・初期値復帰と変更通知", () =>
{
    var service = new ShortcutService(); int changes = 0; service.Changed += () => changes++;
    service.ApplyJson(Json("{\"focusAddress\":[\"Ctrl+K\"],\"switchPane\":[]}"));
    Equal(CommandIds.FocusAddress, service.Map.Resolve(Key("Ctrl+K"), InputScope.Address));
    Equal<string?>(null, service.Map.Resolve(Key("Ctrl+L"), InputScope.Address));
    Equal<string?>(null, service.Map.Resolve(Key("F6"), InputScope.Browser));
    Equal("未割当", service.Map.Display(CommandIds.SwitchPane));
    Equal(1, changes);
    service.Reset(); Equal(2, changes);
    Equal(CommandIds.SwitchPane, service.Map.Resolve(Key("F6"), InputScope.Browser));
});
Check("競合した設定は一部も適用しない", () =>
{
    var service = new ShortcutService();
    service.ApplyJson(Json("{\"focusAddress\":[\"Ctrl+K\"]}"));
    var previous = service.Map;
    Throws<FormatException>(() => service.ApplyJson(Json("{\"focusAddress\":[\"F6\"]}")));
    Equal(previous, service.Map);
    Equal(CommandIds.FocusAddress, service.Map.Resolve(Key("Ctrl+K"), InputScope.Browser));
});
Check("重複・不明なコマンドと壊れた形式を拒否", () =>
{
    foreach (var json in new[] {
        Json("{\"typo\":[\"F8\"]}"), Json("{\"switchPane\":[\"F8\"],\"switchPane\":[\"F9\"]}"),
        Json("{\"focusAddress\":null}"), "{\"version\":\"1\",\"bindings\":{}}", "{\"version\":2,\"bindings\":{}}",
        Json("{\"switchPane\":[\"ctrl+k\",\"CTRL+K\"]}") })
        Throws<FormatException>(() => ShortcutMap.FromJson(json));
});
Check("文字編集・シェル・OSのキーを奪わない", () =>
{
    foreach (var gesture in new[] { "A", "Shift+A", "Space", "Ctrl+Space", "Ctrl+A", "Ctrl+C", "Ctrl+V", "Ctrl+X", "F2", "F5", "Shift+F10", "Tab", "Enter", "Alt+F4", "Alt+Space", "Ctrl+Alt+K" })
        Throws<FormatException>(() => new ShortcutMap(new Dictionary<string, string[]> { [CommandIds.SwitchPane] = [gesture] }));
});
Check("角括弧のキーは保存でき、単独キーはパス入力を奪わない", () =>
{
    var map = new ShortcutMap(new Dictionary<string, string[]> { [CommandIds.Back] = ["[", "Ctrl+["], [CommandIds.Forward] = ["]", "Alt+]"] });
    Equal(CommandIds.Back, map.Resolve(new(0xDB, KeyModifiers.None), InputScope.Browser));
    Equal(CommandIds.Forward, map.Resolve(new(0xDD, KeyModifiers.None), InputScope.Chrome));
    Equal<string?>(null, map.Resolve(Key("["), InputScope.Address));
    Equal(CommandIds.Back, map.Resolve(Key("Ctrl+["), InputScope.Address));
    Equal("[ / Ctrl+[", InputSettings.FromJson(new InputSettings(map, new()).ToJson()).Shortcuts.Display(CommandIds.Back));
    Equal("Alt+]", Key("Alt+]").ToString());
    Throws<FormatException>(() => new ShortcutMap(new Dictionary<string, string[]> { [CommandIds.Back] = ["["], [CommandIds.Forward] = ["["] }));
    Throws<FormatException>(() => new ShortcutMap(new Dictionary<string, string[]> { [CommandIds.NavigateAddress] = ["["] }));
});
Check("入力表記の正規化", () =>
{
    Equal(Key("Ctrl+Shift+K"), Key("shift + CTRL + k"));
    Equal("Ctrl+Shift+K", Key("shift + CTRL + k").ToString());
    Throws<FormatException>(() => Key("Ctrl+Ctrl+K"));
    Throws<FormatException>(() => Key("Win+K"));
});
Check("履歴要求は成功まで現在地を変えず失敗後に再試行できる", () =>
{
    var tab = new TabState(@"C:\A");
    var navigation = new TabNavigation(tab);
    navigation.Complete(@"C:\A"); navigation.Complete(@"C:\B");
    Equal(@"C:\A", navigation.BeginHistory(-1));
    Equal(@"C:\B", tab.CurrentPath);
    navigation.Fail("見つかりません");
    Equal(1, tab.History.Index);
    Equal(@"C:\A", navigation.BeginHistory(-1));
    navigation.Complete(@"c:\a\");
    Equal(0, tab.History.Index); Equal(2, tab.History.Entries.Count);
    Equal<string?>(null, navigation.BeginHistory(-1));
    Equal(@"C:\B", navigation.BeginHistory(1));
    navigation.Complete(@"C:\redirect");
    Equal(@"C:\redirect", tab.CurrentPath); Equal(false, tab.History.CanGoForward);
});
Check("タブと履歴のキーを変更でき編集キーは保護する", () =>
{
    var map = new ShortcutMap();
    Equal(CommandIds.NextTab, map.Resolve(Key("Ctrl+Tab"), InputScope.Browser));
    Equal(CommandIds.PreviousTab, map.Resolve(Key("Ctrl+Shift+Tab"), InputScope.Address));
    Equal(CommandIds.Back, map.Resolve(Key("Alt+Left"), InputScope.Chrome));
    Equal(CommandIds.Parent, map.Resolve(Key("Alt+Up"), InputScope.Browser));
    map = ShortcutMap.FromJson(Json("{\"back\":[\"F8\"],\"nextTab\":[\"Ctrl+J\"]}"));
    Equal<string?>(null, map.Resolve(Key("Alt+Left"), InputScope.Browser));
    Equal(CommandIds.Back, map.Resolve(Key("F8"), InputScope.Browser));
    Equal(CommandIds.NextTab, map.Resolve(Key("Ctrl+J"), InputScope.Address));
    map = ShortcutMap.FromJson(Json("{\"previousTab\":[\"Ctrl+PageUp\"],\"nextTab\":[\"Ctrl+PageDown\"]}"));
    var restored = InputSettings.FromJson(new InputSettings(map, new()).ToJson()).Shortcuts;
    Equal(CommandIds.PreviousTab, restored.Resolve(new(0x21, KeyModifiers.Control), InputScope.Browser));
    Equal(CommandIds.NextTab, restored.Resolve(new(0x22, KeyModifiers.Control), InputScope.Address));
    Equal("Ctrl+PageUp", restored.Display(CommandIds.PreviousTab));
    Throws<FormatException>(() => new ShortcutMap(new Dictionary<string, string[]> { [CommandIds.PreviousTab] = ["Ctrl+PageUp"], [CommandIds.NextTab] = ["Ctrl+PageUp"] }));
    foreach (var gesture in new[] { "Alt+Tab", "Shift+Tab", "Left", "Ctrl+Left", "Shift+Left", "Delete", "Ctrl+Back", "PageUp", "PageDown", "Shift+PageUp", "Ctrl+Shift+PageDown" })
        Throws<FormatException>(() => new ShortcutMap(new Dictionary<string, string[]> { [CommandIds.Back] = [gesture] }));
});
Check("並べ替えは選択・タブID・編集中のパス・履歴を保持する", () =>
{
    var pane = new PaneState("A"); var a = pane.SelectedTab;
    var b = pane.AddTab("B"); var c = pane.AddTab("C");
    b.NavigationSucceeded("B"); b.NavigationSucceeded("B2"); b.AddressText = "editing";
    pane.SelectTab(b); var id = b.Id;
    pane.MoveTab(b, 0);
    Equal(b, pane.Tabs[0]); Equal(b, pane.SelectedTab); Equal(id, b.Id);
    Equal("editing", b.AddressText); Equal(2, b.History.Entries.Count);
    pane.MoveTab(b, 2); Equal(b, pane.Tabs[2]);
    pane.MoveTab(b, 2); Equal(3, pane.Tabs.Count);
    Throws<ArgumentException>(() => pane.MoveTab(new TabState("foreign"), 0));
    Throws<ArgumentOutOfRangeException>(() => pane.MoveTab(b, 3));
    Equal(true, pane.CloseTab(a)); Equal(b, pane.SelectedTab);
    Equal(true, pane.CloseTab(b)); Equal(c, pane.SelectedTab);
    Equal(false, pane.CloseTab(c));
});
Check("表示中のタブを閉じると並び順によらず直前のタブへ戻る", () =>
{
    var pane = new PaneState("A"); var a = pane.SelectedTab;
    var b = pane.AddTab("B"); var c = pane.AddTab("C"); var d = pane.AddTab("D");
    pane.SelectTab(c); pane.SelectTab(b); pane.SelectTab(a); pane.SelectTab(a);
    pane.MoveTab(b, 3);
    Equal(true, pane.CloseTab(a)); Equal(b, pane.SelectedTab);
    Equal(true, pane.CloseTab(b)); Equal(c, pane.SelectedTab);
    Equal(true, pane.CloseTab(c)); Equal(d, pane.SelectedTab);
    Equal(false, pane.CloseTab(d)); Equal(d, pane.SelectedTab);
});
Check("再表示したタブは最新の表示順になり連続で閉じても戻れる", () =>
{
    var pane = new PaneState("A"); var a = pane.SelectedTab;
    var b = pane.AddTab("B"); var c = pane.AddTab("C");
    pane.SelectTab(b); pane.SelectTab(c); pane.SelectTab(a); pane.SelectTab(c);
    Equal(true, pane.CloseTab(c)); Equal(a, pane.SelectedTab);
    Equal(true, pane.CloseTab(a)); Equal(b, pane.SelectedTab);
});
Check("非表示のタブを閉じても表示は変わらず閉じたタブは戻り先から除く", () =>
{
    var pane = new PaneState("A"); var a = pane.SelectedTab;
    var b = pane.AddTab("B"); var c = pane.AddTab("C"); var d = pane.AddTab("D");
    pane.SelectTab(c); pane.SelectTab(b);
    Equal(true, pane.CloseTab(c)); Equal(b, pane.SelectedTab);
    Equal(true, pane.CloseTab(d)); Equal(b, pane.SelectedTab);
    Equal(false, pane.CloseTab(c));
    Equal(false, pane.CloseTab(new TabState("foreign")));
    Equal(true, pane.CloseTab(b)); Equal(a, pane.SelectedTab);
});
Check("タブの表示順は左右のペインで独立する", () =>
{
    var state = new WorkspaceState("L", "R");
    var left = state.Left.SelectedTab; var right = state.Right.SelectedTab;
    var l2 = state.Left.AddTab("L2"); var l3 = state.Left.AddTab("L3");
    var r2 = state.Right.AddTab("R2"); var r3 = state.Right.AddTab("R3");
    state.Left.SelectTab(l3); state.Right.SelectTab(r2);
    state.Left.SelectTab(left); state.Right.SelectTab(r3);
    Equal(true, state.Left.CloseTab(left)); Equal(l3, state.Left.SelectedTab);
    Equal(r3, state.Right.SelectedTab);
    Equal(true, state.Right.CloseTab(r3)); Equal(r2, state.Right.SelectedTab);
    Equal(l3, state.Left.SelectedTab);
});
Check("マウス設定の変更・無効化と省略時の初期値", () =>
{
    Equal(TabCloseButton.Middle, MouseSettings.FromJson("{\"version\":1}").CloseTabButton);
    foreach (var pair in new[] { ("none", TabCloseButton.None), ("middle", TabCloseButton.Middle), ("right", TabCloseButton.Right), ("xButton1", TabCloseButton.XButton1), ("xButton2", TabCloseButton.XButton2) })
        Equal(pair.Item2, MouseSettings.FromJson("{\"version\":1,\"closeTabButton\":\"" + pair.Item1 + "\"}").CloseTabButton);
});
Check("マウス設定の誤記・重複・競合する左ボタンを拒否する", () =>
{
    foreach (var json in new[] { "[]", "{}", "{\"version\":\"1\"}", "{\"version\":2}", "{\"version\":1,\"unknown\":0}",
        "{\"version\":1,\"closeTabButton\":\"left\"}", "{\"version\":1,\"closeTabButton\":null}",
        "{\"version\":1,\"closeTabButton\":\"right\",\"closeTabButton\":\"middle\"}" })
        Throws<FormatException>(() => MouseSettings.FromJson(json));
});
Check("QuickLookは一覧だけで有効、変更・解除とSpace予約を維持", () =>
{
    var map = new ShortcutMap();
    Equal(CommandIds.QuickView, map.Resolve(Key("Space"), InputScope.Browser));
    Equal<string?>(null, map.Resolve(Key("Space"), InputScope.Address));
    Equal<string?>(null, map.Resolve(Key("Space"), InputScope.Chrome));
    var changed = ShortcutMap.FromJson(Json("{\"quickView\":[\"F8\"]}"));
    Equal<string?>(null, changed.Resolve(Key("Space"), InputScope.Browser));
    Equal(CommandIds.QuickView, changed.Resolve(Key("F8"), InputScope.Browser));
    Equal<string?>(null, ShortcutMap.FromJson(Json("{\"quickView\":[]}")).Resolve(Key("Space"), InputScope.Browser));
    Throws<FormatException>(() => ShortcutMap.FromJson(Json("{\"quickView\":[\"Ctrl+Space\"]}")));
    Throws<FormatException>(() => ShortcutMap.FromJson(Json("{\"newTab\":[\"Space\"]}")));
});
Check("QuickLook起動先の自動検出・手動設定と不正設定", () =>
{
    Equal<string?>(null, QuickLookSettings.FromJson("{\"version\":1}").ExecutablePath);
    Equal<string?>(null, QuickLookSettings.FromJson("{\"version\":1,\"executablePath\":null}").ExecutablePath);
    Equal(@"C:\日本語 path\QuickLook.exe", QuickLookSettings.FromJson("{\"version\":1,\"executablePath\":\"C:\\\\日本語 path\\\\QuickLook.exe\"}").ExecutablePath);
    foreach (var json in new[] { "[]", "{}", "{\"version\":\"1\"}", "{\"version\":2}", "{\"version\":1,\"oops\":1}", "{\"version\":1,\"executablePath\":5}",
        "{\"version\":1,\"executablePath\":\"relative.exe\"}", "{\"version\":1,\"executablePath\":null,\"executablePath\":null}" })
        Throws<FormatException>(() => QuickLookSettings.FromJson(json));
});
Check("ブックマークの登録・重複防止・改名・削除と幅の検証", () =>
{
    var sidebar = new SidebarState();
    var bookmark = sidebar.Add(" 作業 ", @"C:\work");
    Equal("作業", bookmark.Name);
    Equal(bookmark, sidebar.Add("別名", @"c:\work\"));
    Equal(1, sidebar.Bookmarks.Count);
    bookmark.Rename(" 資料 "); Equal("資料", bookmark.Name); Equal(@"C:\work", bookmark.Path);
    Equal(@"資料 (C:\work)", bookmark.DisplayText);
    Throws<ArgumentException>(() => bookmark.Rename(" "));
    Equal(true, sidebar.Remove(bookmark)); Equal(false, sidebar.Remove(bookmark));
    Throws<ArgumentOutOfRangeException>(() => sidebar.Width = double.NaN);
    Throws<ArgumentOutOfRangeException>(() => sidebar.Width = 10);
    sidebar.Width = 300; Equal(300d, sidebar.Width);
    Equal(75d, new DriveSnapshot("C", "C", 100, 25).UsedPercent);
    Equal(0d, new DriveSnapshot("C", "C", 0, 0).UsedPercent);
});
Check("容量取得はドライブごとに独立し重複要求・切断後の古い結果を防ぐ", () =>
{
    string[] paths = ["slow", "fast"];
    var pending = new TaskCompletionSource<DriveSnapshot>();
    var calls = 0;
    var updates = new List<DriveSnapshot>();
    using var monitor = new DriveMonitor(() => Task.FromResult(paths), path =>
    {
        if (path == "slow") { calls++; return calls == 1 ? pending.Task : Task.FromResult(new DriveSnapshot(path, "new volume", 300, 100)); }
        return Task.FromResult(new DriveSnapshot(path, path, 100, 25));
    });
    monitor.Updated += updates.Add;
    monitor.RefreshAsync().GetAwaiter().GetResult();
    Equal(1, updates.Count); Equal("fast", updates[0].Path);
    monitor.RefreshAsync().GetAwaiter().GetResult(); Equal(1, calls);
    paths = ["fast"]; monitor.RefreshAsync().GetAwaiter().GetResult();
    paths = ["slow", "fast"]; monitor.RefreshAsync().GetAwaiter().GetResult(); Equal(1, calls);
    pending.SetResult(new("slow", "old volume", 200, 10));
    Equal(true, SpinWait.SpinUntil(() => { monitor.RefreshAsync().GetAwaiter().GetResult(); return calls == 2; }, 2000));
    Equal(false, updates.Any(d => d.Name == "old volume"));
    Equal(true, updates.Any(d => d.Name == "new volume"));
    monitor.Dispose();
    var before = updates.Count;
    monitor.RefreshAsync().GetAwaiter().GetResult(); Equal(before, updates.Count);
});
Check("容量取得の失敗を他ドライブへ波及させない", () =>
{
    var updates = new List<DriveSnapshot>();
    using var monitor = new DriveMonitor(() => Task.FromResult(new[] { "denied", "ready" }), path =>
        path == "denied" ? Task.FromException<DriveSnapshot>(new UnauthorizedAccessException("アクセスできません")) : Task.FromResult(new DriveSnapshot(path, path, 100, 75)));
    monitor.Updated += updates.Add;
    monitor.RefreshAsync().GetAwaiter().GetResult();
    Equal(2, updates.Count);
    Equal("アクセスできません", updates.Single(d => d.Path == "denied").Error);
    Equal(75L, updates.Single(d => d.Path == "ready").FreeBytes);
});
Check("未接続ドライブも更新対象に残し、次の更新で利用可能へ変わる", () =>
{
    var calls = 0;
    var updates = new List<DriveSnapshot>();
    using var monitor = new DriveMonitor(() => Task.FromResult(new[] { "removable" }), path =>
        Task.FromResult(++calls == 1 ? new DriveSnapshot(path, path, null, null, Availability: DriveAvailability.Unavailable) : new DriveSnapshot(path, "connected", 100, 80)));
    monitor.Updated += updates.Add;
    monitor.RefreshAsync().GetAwaiter().GetResult();
    monitor.RefreshAsync().GetAwaiter().GetResult();
    Equal(2, calls); Equal(DriveAvailability.Unavailable, updates[0].Availability); Equal(DriveAvailability.Ready, updates[1].Availability);
});
Check("作業状態は順序・選択・幅・ブックマークを復元し、編集文字列と履歴は保存しない", () =>
{
    var state = new WorkspaceState(@"C:\first", @"\\wsl.localhost\Ubuntu\home\missing");
    var tab = state.Left.AddTab(@"C:\second"); tab.NavigationSucceeded(@"C:\visited"); tab.AddressText = @"C:\未確定";
    state.Left.MoveTab(tab, 0); state.Left.SelectTab(tab); state.Activate(state.Right);
    state.LeftPaneRatio = 0.65; state.Sidebar.Width = 320;
    state.Sidebar.Add("Case", @"\\wsl$\Ubuntu\home\Case"); state.Sidebar.Add("case", @"\\wsl$\Ubuntu\home\case");
    var restored = WorkspaceSnapshot.FromJson(WorkspaceSnapshot.Capture(state).ToJson()).Restore();
    Equal(@"C:\visited", restored.Left.Tabs[0].InitialPath); Equal(0, restored.Left.Tabs.IndexOf(restored.Left.SelectedTab));
    Equal(0, restored.Left.SelectedTab.History.Entries.Count); Equal(@"C:\visited", restored.Left.SelectedTab.AddressText);
    Equal(restored.Right, restored.ActivePane); Equal(0.65, restored.LeftPaneRatio); Equal(320d, restored.Sidebar.Width); Equal(2, restored.Sidebar.Bookmarks.Count);
    Equal(@"\\wsl.localhost\Ubuntu\home\missing", restored.Right.SelectedTab.InitialPath);
    foreach (var b in state.Sidebar.Bookmarks.ToArray()) state.Sidebar.Remove(b);
    Equal(0, WorkspaceSnapshot.FromJson(WorkspaceSnapshot.Capture(state).ToJson()).Restore().Sidebar.Bookmarks.Count);
});
Check("不正な状態と将来バージョンを拒否する", () =>
{
    var snapshot = WorkspaceSnapshot.Capture(new WorkspaceState(@"C:\a", @"C:\b"));
    Throws<FormatException>(() => (snapshot with { Left = new([], 0) }).ToJson());
    Throws<FormatException>(() => (snapshot with { Right = new([@"C:\b"], 1) }).ToJson());
    Throws<FormatException>(() => (snapshot with { SidebarWidth = double.NaN }).ToJson());
    Throws<FormatException>(() => WorkspaceSnapshot.FromJson("[]"));
    Throws<NotSupportedException>(() => WorkspaceSnapshot.FromJson("{\"version\":99}"));
});
Check("状態を安全に置換しバックアップを作り、同時保存を防ぐ", () =>
{
    var directory = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts", "store-" + Guid.NewGuid().ToString("N"))).FullName;
    var path = Path.Combine(directory, "workspace.json");
    var state = new WorkspaceState(@"C:\a", @"C:\b");
    var first = WorkspaceSnapshot.Capture(state).ToJson();
    using (var store = new WorkspaceStore(path))
    {
        Equal<WorkspaceSnapshot?>(null, store.Load().Snapshot); store.Save(first);
        state.Sidebar.Width = 330; store.Save(WorkspaceSnapshot.Capture(state).ToJson());
        Equal(first, File.ReadAllText(path + ".bak")); Equal(0, Directory.GetFiles(directory, "*.tmp-*").Length);
        using var second = new WorkspaceStore(path); Equal(true, second.Load().Warning != null); Equal(false, second.CanSave);
        Throws<InvalidOperationException>(() => second.Save(first));
    }
    using var reopened = new WorkspaceStore(path); Equal(330d, reopened.Load().Snapshot!.SidebarWidth);
});
Check("破損した状態は原本を退避してバックアップから回復し、将来形式を上書きしない", () =>
{
    var directory = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts", "recovery-" + Guid.NewGuid().ToString("N"))).FullName;
    var path = Path.Combine(directory, "workspace.json");
    var json = WorkspaceSnapshot.Capture(new WorkspaceState(@"C:\a", @"C:\b")).ToJson();
    File.WriteAllText(path, "broken"); File.WriteAllText(path + ".bak", json);
    using (var store = new WorkspaceStore(path))
    {
        var loaded = store.Load(); Equal(true, loaded.Warning != null); Equal(json, loaded.Snapshot!.ToJson());
        Equal("broken", File.ReadAllText(Directory.GetFiles(directory, "*.invalid-*").Single()));
        store.Save(json); Equal(json, File.ReadAllText(path));
    }
    File.WriteAllText(path, "{\"version\":99}");
    using var future = new WorkspaceStore(path); Equal(true, future.Load().Warning != null); Equal(false, future.CanSave);
    Equal("{\"version\":99}", File.ReadAllText(path));
});
Check("置換に失敗しても前の状態を壊さず、一時ファイルを片付けて再試行できる", () =>
{
    var directory = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts", "save-failure-" + Guid.NewGuid().ToString("N"))).FullName;
    var path = Path.Combine(directory, "workspace.json");
    var state = new WorkspaceState(@"C:\a", @"C:\b"); var first = WorkspaceSnapshot.Capture(state).ToJson();
    using var store = new WorkspaceStore(path); store.Load(); store.Save(first);
    state.Sidebar.Width = 350; var changed = WorkspaceSnapshot.Capture(state).ToJson();
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Throws<IOException>(() => store.Save(changed)); Equal(first, File.ReadAllText(path));
        Equal(0, Directory.GetFiles(directory, "*.tmp-*").Length);
    }
    store.Save(changed); Equal(changed, File.ReadAllText(path));
});
Check("ウィンドウの保存は旧形式と互換性を保ち、通常矩形と最大化を往復する", () =>
{
    var original = WorkspaceSnapshot.Capture(new WorkspaceState(@"C:\a", @"C:\b"));
    var oldJson = original.ToJson().Replace(",\r\n  \"window\": null", "").Replace(",\n  \"window\": null", "");
    Equal<WindowSnapshot?>(null, WorkspaceSnapshot.FromJson(oldJson).Window);
    var placed = original with { Window = new(-1400, 40, 1100, 700, true) };
    Equal(placed.Window, WorkspaceSnapshot.FromJson(placed.ToJson()).Window);
    Throws<FormatException>(() => (original with { Window = new(0, 0, 0, 700, false) }).ToJson());
});
Check("画面外と過大サイズを作業領域へ補正し、負座標のモニターを維持する", () =>
{
    var onLeftMonitor = new WindowSnapshot(-1800, 50, 1000, 700, true);
    Equal(onLeftMonitor, onLeftMonitor.FitToWorkArea(-1920, 30, 1920, 1050));
    Equal(new WindowSnapshot(0, 40, 1280, 680, false), new WindowSnapshot(500000, -500000, 8000, 8000, false).FitToWorkArea(0, 40, 1280, 680));
    Equal(new WindowSnapshot(280, 0, 1000, 600, true), new WindowSnapshot(5000, -400, 1000, 600, true).FitToWorkArea(0, 0, 1280, 720));
});
Check("入力設定は解除・複数割当・マウス・QuickLookをまとめて往復する", () =>
{
    var settings = new InputSettings(new(new Dictionary<string, string[]> { [CommandIds.Copy] = ["F7"], [CommandIds.QuickView] = [], [CommandIds.NewTab] = ["F8", "Ctrl+T"] }), new(TabCloseButton.Right), new(@"C:\Apps\QuickLook.exe"));
    var restored = InputSettings.FromJson(settings.ToJson());
    Equal(settings.ToJson(), restored.ToJson());
    Equal<string?>(null, restored.Shortcuts.Resolve(ShortcutGesture.Parse("Ctrl+C"), InputScope.Browser));
    Equal(CommandIds.Copy, restored.Shortcuts.Resolve(ShortcutGesture.Parse("F7"), InputScope.Browser));
    Equal<string?>(null, restored.Shortcuts.Resolve(ShortcutGesture.Parse("F7"), InputScope.Address));
    Equal("未割当", restored.Shortcuts.Display(CommandIds.QuickView));
    Throws<FormatException>(() => InputSettings.FromJson(settings.ToJson().Replace("\"right\"", "\"left\"")));
    Throws<FormatException>(() => InputSettings.FromJson("{\"version\":\"1\"}"));
    Throws<FormatException>(() => InputSettings.FromJson(settings.ToJson().Replace("\"F7\"", "\"Ctrl+W\"")));
});
Check("ブックマークの新規タブ設定は旧形式を読み込み、独立して保存・変更・無効化できる", () =>
{
    Equal(TabCloseButton.Middle, MouseSettings.FromJson("{\"version\":1,\"closeTabButton\":\"right\"}").OpenBookmarkInNewTabButton);
    foreach (var button in Enum.GetValues<TabCloseButton>())
    {
        var mouse = MouseSettings.FromJson("{\"version\":1,\"openBookmarkInNewTabButton\":\"" + MouseSettings.ButtonName(button) + "\"}");
        Equal(TabCloseButton.Middle, mouse.CloseTabButton);
        Equal(button, mouse.OpenBookmarkInNewTabButton);
        var settings = new InputSettings(new(), mouse);
        Equal(mouse, InputSettings.FromJson(settings.ToJson()).Mouse);
    }
    foreach (var value in new[] { "\"left\"", "null", "1", "\"unknown\"" })
        Throws<FormatException>(() => MouseSettings.FromJson("{\"version\":1,\"openBookmarkInNewTabButton\":" + value + "}"));
    Throws<FormatException>(() => MouseSettings.FromJson("{\"version\":1,\"openBookmarkInNewTabButton\":\"right\",\"openBookmarkInNewTabButton\":\"middle\"}"));
});
Console.WriteLine($"{count - failures.Count}/{count} passed");
return failures.Count == 0 ? 0 : 1;
