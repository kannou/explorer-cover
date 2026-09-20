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
    Equal<string?>(null, map.Resolve(Key("Ctrl+C"), InputScope.Browser));
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
    foreach (var gesture in new[] { "Alt+Tab", "Shift+Tab", "Left", "Ctrl+Left", "Shift+Left", "Delete", "Ctrl+Back" })
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
Console.WriteLine($"{count - failures.Count}/{count} passed");
return failures.Count == 0 ? 0 : 1;
