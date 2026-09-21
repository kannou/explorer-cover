using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ExplorerCover.Core;
using ExplorerCover.Shell;
using ExplorerCover.Commands;
using ShortcutScope = ExplorerCover.Core.InputScope;

namespace ExplorerCover;

public sealed class MainWindow : Window
{
    private readonly BrowserPane left;
    private readonly BrowserPane right;
    private readonly SidebarView sidebar;
    private readonly CommandDispatcher commands = new();
    private readonly ShortcutService shortcuts;
    private readonly TextBlock help;
    private ShellDialogFocus? shellDialogFocus;
    private BrowserPane? shellInputOrigin;
    private QuickLookClient quickLook;
    private QuickLookSettings quickLookSettings;
    private MouseSettings mouseSettings;
    private readonly CancellationTokenSource lifetime = new();
    private bool previewPending;
    private int previewKey = 0x20;
    private readonly System.Windows.Threading.DispatcherTimer previewSelectionTimer;
    private Task selectionUpdate = Task.CompletedTask;
    private bool previewTracking;
    private string? lastPreviewSelection;
    public WorkspaceState State { get; }
    private readonly TextBlock saveWarning = new() { Foreground = Brushes.Firebrick, Margin = new Thickness(10, 4, 10, 4), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
    public void ShowSaveWarning(string? message) { saveWarning.Text = message ?? ""; saveWarning.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible; }

    public MainWindow(string leftPath, string rightPath, ShortcutService shortcuts, string? settingsWarning = null, MouseSettings? mouseSettings = null, QuickLookSettings? quickLookSettings = null, WorkspaceState? restored = null)
    {
        State = restored ?? new(leftPath, rightPath);
        var initialActivePane = State.ActivePane;
        this.shortcuts = shortcuts;
        this.mouseSettings = mouseSettings ?? new();
        this.quickLookSettings = quickLookSettings ?? new();
        quickLook = new(this.quickLookSettings);
        previewSelectionTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        previewSelectionTimer.Tick += PreviewSelectionChanged;
        previewSelectionTimer.Start();
        Title = ProductInfo.Name;
        Width = Math.Min(1400, SystemParameters.WorkArea.Width); Height = 740; MinWidth = 900; MinHeight = 380;
        FontFamily = new FontFamily("Yu Gothic UI"); FontSize = 13;
        var root = new DockPanel();
        help = new TextBlock { Margin = new Thickness(10, 7, 10, 7), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
        var footer = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var aboutButton = new Button { Content = "ⓘ", ToolTip = "バージョン情報", Width = 34, Margin = new Thickness(4), FontSize = 18, Padding = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(aboutButton, "OpenAbout");
        System.Windows.Automation.AutomationProperties.SetName(aboutButton, "バージョン情報");
        aboutButton.Click += (_, _) => MessageBox.Show(this, $"{ProductInfo.Name}\nバージョン {ProductInfo.Version}\nファイル版 {ProductInfo.FileVersion}", "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
        DockPanel.SetDock(aboutButton, Dock.Left); footer.Children.Add(aboutButton);
        var settingsButton = new Button { Content = "⚙", ToolTip = "設定", Width = 34, Margin = new Thickness(4), FontSize = 18, Padding = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(settingsButton, "OpenSettings");
        System.Windows.Automation.AutomationProperties.SetName(settingsButton, "設定");
        settingsButton.Click += (_, _) => new SettingsWindow(new(shortcuts.Map, this.mouseSettings, this.quickLookSettings), ApplyInputSettings) { Owner = this }.ShowDialog();
        DockPanel.SetDock(settingsButton, Dock.Left); footer.Children.Add(settingsButton); footer.Children.Add(help);
        DockPanel.SetDock(saveWarning, Dock.Bottom); root.Children.Add(saveWarning);
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
        System.Windows.Automation.AutomationProperties.SetAutomationId(splitter, "Pane.Splitter");
        splitter.DragCompleted += (_, _) =>
        {
            var total = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
            if (total > 0) State.LeftPaneRatio = grid.ColumnDefinitions[0].ActualWidth / total;
        };
        Grid.SetColumn(splitter, 1); grid.Children.Add(splitter);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(State.Sidebar.Width), MinWidth = 160, MaxWidth = 380 });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        sidebar = new(State, path => ViewFor(State.ActivePane).Navigate(path), restored == null,
            path => ViewFor(State.ActivePane).OpenInNewTab(path), this.mouseSettings);
        layout.Children.Add(sidebar);
        var sidebarSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.LightGray, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        System.Windows.Automation.AutomationProperties.SetAutomationId(sidebarSplitter, "Sidebar.Splitter");
        sidebarSplitter.DragCompleted += (_, _) => State.Sidebar.Width = Math.Clamp(layout.ColumnDefinitions[0].ActualWidth, 160, 380);
        Grid.SetColumn(sidebarSplitter, 1); layout.Children.Add(sidebarSplitter);
        Grid.SetColumn(grid, 2); layout.Children.Add(grid);
        root.Children.Add(layout); Content = root;
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
        foreach (var verb in new[] { CommandIds.Copy, CommandIds.Cut, CommandIds.Paste, CommandIds.Delete, CommandIds.Rename })
            Register(verb, v =>
            {
                var tab = v.State.SelectedTab;
                var browser = v.Browser;
                var path = browser.CurrentPath;
                try { browser.ExecuteShellCommand(verb); }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or NotImplementedException or UnauthorizedAccessException or ArgumentException) { v.ShowOperationMessage("操作できません: " + ex.Message); }
                finally
                {
                    if (verb == CommandIds.Delete)
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
                        {
                            // Shellの確認画面とWPFのフォーカス復元が終わってから、元の一覧へ戻す。
                            // 待機中に移動・タブ切替・別ペインへの操作があればフォーカスを奪わない。
                            if (lifetime.IsCancellationRequested || !IsActive || !IsEnabled ||
                                State.ActivePane != v.State || v.State.SelectedTab != tab ||
                                browser.IsNavigating || browser.CurrentPath != path) return;
                            browser.FocusView();
                        });
                }
            }, v => !v.Browser.IsNavigating);
        shortcuts.Changed += UpdateHelp;
        UpdateActivePane(); UpdateHelp();
        Loaded += (_, _) => { State.Activate(initialActivePane); ViewFor(initialActivePane).FocusAddress(); };
        SourceInitialized += (_, _) => shellDialogFocus = new ShellDialogFocus(new WindowInteropHelper(this).Handle, Dispatcher, () =>
        {
            var pane = shellInputOrigin;
            if (pane == null) return null; // 設定画面などWPFの操作によるダイアログは対象外。
            var tab = pane.State.SelectedTab;
            var browser = pane.Browser;
            var path = browser.CurrentPath;
            return () =>
            {
                if (lifetime.IsCancellationRequested || !IsActive || !IsEnabled ||
                    State.ActivePane != pane.State || pane.State.SelectedTab != tab ||
                    browser.IsNavigating || browser.CurrentPath != path) return;
                browser.FocusView();
                DiagnosticLog.Write("Shell dialog focus restored");
            };
        });
        ComponentDispatcher.ThreadFilterMessage += FilterMessage;
        PreviewKeyDown += HandleWpfKey;
        PreviewMouseDown += (_, _) => shellInputOrigin = null;
        Closed += (_, _) =>
        {
            shellDialogFocus?.Dispose();
            lifetime.Cancel();
            previewSelectionTimer.Stop();
            previewSelectionTimer.Tick -= PreviewSelectionChanged;
            ComponentDispatcher.ThreadFilterMessage -= FilterMessage;
            shortcuts.Changed -= UpdateHelp;
            sidebar.Dispose(); left.Dispose(); right.Dispose();
        };
    }

    private BrowserPane ViewFor(PaneState pane) => pane == State.Left ? left : pane == State.Right ? right : throw new ArgumentException("不明なペインです。");
    private void Register(string id, Action<BrowserPane> run, Predicate<BrowserPane>? canRun = null) =>
        commands.Register(id, pane => { State.Activate(pane); run(ViewFor(pane)); }, pane => canRun?.Invoke(ViewFor(pane)) ?? true);
    private void ApplyInputSettings(InputSettings settings)
    {
        shortcuts.ApplyJson(InputSettings.ShortcutJson(settings.Shortcuts));
        mouseSettings = settings.Mouse; left.ApplyMouseSettings(mouseSettings); right.ApplyMouseSettings(mouseSettings);
        sidebar.ApplyMouseSettings(mouseSettings);
        quickLookSettings = settings.QuickLook ?? new(); quickLook = new(quickLookSettings);
    }
    private BrowserPane? NativeFocusedPane => left.Browser.ContainsNativeFocus ? left : right.Browser.ContainsNativeFocus ? right : null;
    private void UpdateActivePane() { left.SetActive(State.ActivePane == State.Left); right.SetActive(State.ActivePane == State.Right); }
    private void UpdateHelp() => help.Text = $"{shortcuts.Map.Display(CommandIds.FocusAddress)}: パス入力  ·  {shortcuts.Map.Display(CommandIds.SwitchPane)}: 左右切替  ·  {shortcuts.Map.Display(CommandIds.NewTab)}: 新しいタブ  ·  {shortcuts.Map.Display(CommandIds.CloseTab)}: 閉じる  ·  {shortcuts.Map.Display(CommandIds.NextTab)}: 次のタブ  ·  {shortcuts.Map.Display(CommandIds.Back)} / {shortcuts.Map.Display(CommandIds.Forward)}: 戻る／進む  ·  {shortcuts.Map.Display(CommandIds.QuickView)}: QuickLook";

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
        if (msg.message is 0x100 or 0x104 or 0x201 or 0x204) // キー、左右ボタンの押下
        {
            foreach (var pane in new[] { left, right })
            {
                var hwnd = pane.Browser.Handle;
                if (hwnd != 0 && (msg.hwnd == hwnd || Native.IsChild(hwnd, msg.hwnd)))
                { shellInputOrigin = pane; break; }
            }
        }
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
            else if (msg.message == 0x100 && ((int)msg.wParam is 0x25 or 0x27) && Native.GetKeyState(0x12) >= 0)
            {
                // 未処理で返すとWPFの方向キーによるフォーカス移動に回るため、
                // 名前編集欄へ直接届ける。Ctrl/Shiftの単語移動・範囲選択もEditに任せる。
                Native.SendMessage(Native.GetFocus(), (uint)msg.message, msg.wParam, msg.lParam);
                handled = true;
            }
            return;
        }
        var modifiers = (Native.GetKeyState(0x11) < 0 ? KeyModifiers.Control : 0) |
            (Native.GetKeyState(0x10) < 0 ? KeyModifiers.Shift : 0) | (Native.GetKeyState(0x12) < 0 ? KeyModifiers.Alt : 0);
        if (msg.wParam == 0x09 && (modifiers & ~KeyModifiers.Shift) == 0)
        { commands.Execute(CommandIds.FocusAddress, source.State); handled = true; return; }
        handled = DispatchShortcut(new((int)msg.wParam, modifiers), ShortcutScope.Browser, source.State, (msg.lParam.ToInt64() & (1L << 30)) != 0);
        if (!handled)
        {
            var gesture = new ShortcutGesture((int)msg.wParam, modifiers);
            // 設定で解除・変更した標準キーをShellへ落として二重の割り当てにしない。
            handled = CommandCatalog.All.Where(c => c.Id is CommandIds.Copy or CommandIds.Cut or CommandIds.Paste or CommandIds.Delete or CommandIds.Rename)
                .Any(c => ShortcutGesture.Parse(c.DefaultGesture) == gesture);
        }
        if (!handled) handled = source.Browser.TranslateShellKey(ref msg);
    }

    private void HandleWpfKey(object sender, KeyEventArgs e)
    {
        if (NativeFocusedPane == null) shellInputOrigin = null;
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
