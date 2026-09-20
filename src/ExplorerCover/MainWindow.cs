using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ExplorerCover.Core;
using ExplorerCover.Shell;
using ShortcutScope = ExplorerCover.Core.InputScope;

namespace ExplorerCover;

public sealed class MainWindow : Window
{
    private readonly BrowserPane left;
    private readonly BrowserPane right;
    private readonly CommandDispatcher commands = new();
    private readonly ShortcutService shortcuts;
    private readonly TextBlock help;
    private readonly QuickLookClient quickLook;
    private readonly CancellationTokenSource lifetime = new();
    private bool previewPending;
    private int previewKey = 0x20;
    private readonly System.Windows.Threading.DispatcherTimer previewSelectionTimer;
    private Task selectionUpdate = Task.CompletedTask;
    private bool previewTracking;
    private string? lastPreviewSelection;
    public WorkspaceState State { get; }

    public MainWindow(string leftPath, string rightPath, ShortcutService shortcuts, string? settingsWarning = null, MouseSettings? mouseSettings = null, QuickLookSettings? quickLookSettings = null)
    {
        State = new(leftPath, rightPath);
        this.shortcuts = shortcuts;
        quickLook = new(quickLookSettings ?? new());
        previewSelectionTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        previewSelectionTimer.Tick += PreviewSelectionChanged;
        previewSelectionTimer.Start();
        Title = "explorer_cover — 2ペイン試作";
        Width = 1200; Height = 740; MinWidth = 700; MinHeight = 380;
        FontFamily = new FontFamily("Yu Gothic UI"); FontSize = 13;
        var root = new DockPanel();
        help = new TextBlock { Margin = new Thickness(10, 7, 10, 7), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(help, Dock.Bottom); root.Children.Add(help);
        if (settingsWarning != null)
        {
            var warning = new TextBlock { Text = settingsWarning, Foreground = Brushes.Firebrick, Margin = new Thickness(10, 4, 10, 4), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(warning, Dock.Bottom); root.Children.Add(warning);
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(State.LeftPaneRatio, GridUnitType.Star), MinWidth = 250 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - State.LeftPaneRatio, GridUnitType.Star), MinWidth = 250 });
        left = new BrowserPane("左", State.Left, commands, mouseSettings ?? new()); right = new BrowserPane("右", State.Right, commands, mouseSettings ?? new());
        grid.Children.Add(left);
        var splitter = new GridSplitter { Width = 7, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.LightGray, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        splitter.DragCompleted += (_, _) =>
        {
            var total = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
            if (total > 0) State.LeftPaneRatio = grid.ColumnDefinitions[0].ActualWidth / total;
        };
        Grid.SetColumn(splitter, 1); grid.Children.Add(splitter);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        root.Children.Add(grid); Content = root;
        left.Activated += () => State.Activate(State.Left);
        right.Activated += () => State.Activate(State.Right);
        State.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WorkspaceState.ActivePane)) UpdateActivePane(); };
        commands.Register(CommandIds.FocusAddress, pane => { State.Activate(pane); ViewFor(pane).FocusAddress(); });
        commands.Register(CommandIds.FocusFiles, pane => { State.Activate(pane); ViewFor(pane).FocusFiles(); });
        commands.Register(CommandIds.SwitchPane, pane =>
        {
            var other = State.OtherPane(pane);
            State.Activate(other);
            ViewFor(other).FocusFiles();
        });
        commands.Register(CommandIds.NavigateAddress, pane => { State.Activate(pane); ViewFor(pane).NavigateAddress(); },
            pane => ViewFor(pane).CanNavigate && !string.IsNullOrWhiteSpace(pane.SelectedTab.AddressText));
        Register(CommandIds.NewTab, v => v.AddTab(false));
        Register(CommandIds.DuplicateTab, v => v.AddTab(true));
        Register(CommandIds.CloseTab, v => v.CloseTab(), v => v.State.Tabs.Count > 1);
        Register(CommandIds.NextTab, v => v.CycleTab(1), v => v.State.Tabs.Count > 1);
        Register(CommandIds.PreviousTab, v => v.CycleTab(-1), v => v.State.Tabs.Count > 1);
        Register(CommandIds.Back, v => v.NavigateHistory(-1), v => v.CanNavigate && v.State.SelectedTab.History.CanGoBack);
        Register(CommandIds.Forward, v => v.NavigateHistory(1), v => v.CanNavigate && v.State.SelectedTab.History.CanGoForward);
        Register(CommandIds.Parent, v => v.NavigateParent(), v => v.CanNavigate && v.ParentPath != null);
        Register(CommandIds.QuickView, v => _ = PreviewAsync(v, previewKey), v => v.CanNavigate && !previewPending);
        shortcuts.Changed += UpdateHelp;
        UpdateActivePane(); UpdateHelp();
        ComponentDispatcher.ThreadFilterMessage += FilterMessage;
        PreviewKeyDown += HandleWpfKey;
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            previewSelectionTimer.Stop();
            previewSelectionTimer.Tick -= PreviewSelectionChanged;
            ComponentDispatcher.ThreadFilterMessage -= FilterMessage;
            shortcuts.Changed -= UpdateHelp;
            left.Dispose(); right.Dispose();
        };
    }

    private BrowserPane ViewFor(PaneState pane) => pane == State.Left ? left : pane == State.Right ? right : throw new ArgumentException("不明なペインです。");
    private void Register(string id, Action<BrowserPane> run, Predicate<BrowserPane>? canRun = null) =>
        commands.Register(id, pane => { State.Activate(pane); run(ViewFor(pane)); }, pane => canRun?.Invoke(ViewFor(pane)) ?? true);
    private BrowserPane? NativeFocusedPane => left.Browser.ContainsNativeFocus ? left : right.Browser.ContainsNativeFocus ? right : null;
    private void UpdateActivePane() { left.SetActive(State.ActivePane == State.Left); right.SetActive(State.ActivePane == State.Right); }
    private void UpdateHelp() => help.Text = $"{shortcuts.Map.Display(CommandIds.FocusAddress)}: パス入力  ·  {shortcuts.Map.Display(CommandIds.SwitchPane)}: 左右切替  ·  {shortcuts.Map.Display(CommandIds.NewTab)}: 新しいタブ  ·  {shortcuts.Map.Display(CommandIds.CloseTab)}: 閉じる  ·  {shortcuts.Map.Display(CommandIds.NextTab)}: 次のタブ  ·  {shortcuts.Map.Display(CommandIds.Back)} / {shortcuts.Map.Display(CommandIds.Forward)}: 戻る／進む  ·  {shortcuts.Map.Display(CommandIds.QuickView)}: QuickLook  ｜  終了時の状態保存は未実装";

    private async Task PreviewAsync(BrowserPane pane, int key)
    {
        if (previewPending || !pane.Browser.ContainsNativeFocus || Native.IsEditingText() || Native.IsComposingText()) return;
        var path = pane.Browser.GetSingleSelectedFile();
        if (path == null) { DiagnosticLog.Write("QuickLook skipped: select one file"); return; }
        var tab = pane.State.SelectedTab;
        var currentPath = tab.CurrentPath;
        bool StillCurrent() => !lifetime.IsCancellationRequested && IsActive && State.ActivePane == pane.State &&
            pane.State.SelectedTab == tab && tab.CurrentPath == currentPath && pane.CanNavigate &&
            pane.Browser.ContainsNativeFocus && !Native.IsEditingText() && !Native.IsComposingText() &&
            pane.Browser.GetSingleSelectedFile() == path;
        previewPending = true;
        try
        {
            // 更新要求がToggleの後に到着して閉じる操作を邪魔しないよう、順序を保つ。
            await selectionUpdate;
            // キーを離すまで表示しない。リピートがQuickLook側へ流れるのを防ぐ。
            while (Native.GetAsyncKeyState(key) < 0)
            {
                await Task.Delay(25, lifetime.Token);
                if (!StillCurrent()) return;
            }
            if (!StillCurrent()) return;
            pane.ShowOperationMessage(null);
            await quickLook.PreviewAsync(path, () => Dispatcher.Invoke(StillCurrent), lifetime.Token);
            previewTracking = true;
            lastPreviewSelection = path;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException or System.Security.SecurityException)
        {
            DiagnosticLog.Write($"QuickLook failed: {ex.Message}");
            if (!lifetime.IsCancellationRequested && pane.State.SelectedTab == tab && tab.CurrentPath == currentPath)
                pane.ShowOperationMessage($"QuickLookを開けません: {ex.Message}");
        }
        finally { previewPending = false; }
    }

    private void PreviewSelectionChanged(object? sender, EventArgs e)
    {
        if (!previewTracking || previewPending || !selectionUpdate.IsCompleted || !IsActive || lifetime.IsCancellationRequested) return;
        var pane = NativeFocusedPane;
        if (pane == null || !pane.CanNavigate || Native.IsEditingText() || Native.IsComposingText()) return;
        var path = pane.Browser.GetSingleSelectedFile();
        if (path == lastPreviewSelection) return;
        lastPreviewSelection = path;
        if (path == null) return; // 未選択・複数選択・フォルダーでは現在の表示を維持する。
        var tab = pane.State.SelectedTab;
        selectionUpdate = UpdatePreviewSelectionAsync(pane, tab, path);
    }

    private async Task UpdatePreviewSelectionAsync(BrowserPane pane, TabState tab, string path)
    {
        bool StillCurrent() => !lifetime.IsCancellationRequested && !previewPending && IsActive &&
            pane.State.SelectedTab == tab && pane.CanNavigate && pane.Browser.ContainsNativeFocus &&
            !Native.IsEditingText() && !Native.IsComposingText() && pane.Browser.GetSingleSelectedFile() == path;
        try { await quickLook.SwitchAsync(path, () => Dispatcher.Invoke(StillCurrent), lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException or System.Security.SecurityException)
        { DiagnosticLog.Write($"QuickLook selection update failed: {ex.Message}"); }
        finally
        {
            // フォーカス移動等で送信を取り消した場合は、一覧に戻った時に再評価する。
            if (!StillCurrent()) lastPreviewSelection = null;
        }
    }

    private bool DispatchShortcut(ShortcutGesture gesture, ShortcutScope scope, PaneState pane, bool repeated)
    {
        var command = shortcuts.Map.Resolve(gesture, scope);
        if (command == null) return false;
        if (!repeated)
        {
            DiagnosticLog.Write($"Command: {command}; pane={(pane == State.Left ? "left" : "right")}");
            if (command == CommandIds.QuickView) previewKey = gesture.VirtualKey;
            commands.Execute(command, pane);
        }
        return true; // 無効な操作もシェルへ落とさず二重処理を防ぐ。
    }

    private void FilterMessage(ref MSG msg, ref bool handled)
    {
        if (handled || !IsActive || (msg.message != 0x100 && msg.message != 0x104)) return;
        var source = NativeFocusedPane;
        if (source == null) return; // WPFはIMEを認識できるPreviewKeyDownで処理。
        if (Native.GetKeyState(0x5B) < 0 || Native.GetKeyState(0x5C) < 0) return;
        State.Activate(source.State);
        if (Native.IsComposingText()) return;
        if (Native.IsEditingText())
        {
            if (msg.wParam == 0x41 && Native.GetKeyState(0x11) < 0 && Native.GetKeyState(0x12) >= 0)
            {
                Native.SendMessage(Native.GetFocus(), 0xB1, 0, -1);
                handled = true;
            }
            return;
        }
        var modifiers = (Native.GetKeyState(0x11) < 0 ? KeyModifiers.Control : 0) |
            (Native.GetKeyState(0x10) < 0 ? KeyModifiers.Shift : 0) | (Native.GetKeyState(0x12) < 0 ? KeyModifiers.Alt : 0);
        if (msg.wParam == 0x09 && (modifiers & ~KeyModifiers.Shift) == 0)
        { commands.Execute(CommandIds.FocusAddress, source.State); handled = true; return; }
        handled = DispatchShortcut(new((int)msg.wParam, modifiers), ShortcutScope.Browser, source.State, (msg.lParam.ToInt64() & (1L << 30)) != 0);
        if (!handled) handled = source.Browser.TranslateShellKey(ref msg);
    }

    private void HandleWpfKey(object sender, KeyEventArgs e)
    {
        if (e.Handled || NativeFocusedPane != null || e.Key is Key.ImeProcessed or Key.DeadCharProcessed) return;
        var pane = left.Address.IsKeyboardFocusWithin ? left.State : right.Address.IsKeyboardFocusWithin ? right.State : State.ActivePane;
        var scope = left.Address.IsKeyboardFocusWithin || right.Address.IsKeyboardFocusWithin ? ShortcutScope.Address :
            Keyboard.FocusedElement is TextBoxBase or PasswordBox ? ShortcutScope.None : ShortcutScope.Chrome;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var wpfModifiers = Keyboard.Modifiers;
        if (wpfModifiers.HasFlag(ModifierKeys.Windows)) return;
        var modifiers = (wpfModifiers.HasFlag(ModifierKeys.Control) ? KeyModifiers.Control : 0) |
            (wpfModifiers.HasFlag(ModifierKeys.Shift) ? KeyModifiers.Shift : 0) | (wpfModifiers.HasFlag(ModifierKeys.Alt) ? KeyModifiers.Alt : 0);
        e.Handled = DispatchShortcut(new(KeyInterop.VirtualKeyFromKey(key), modifiers), scope, pane, e.IsRepeat);
    }
}
