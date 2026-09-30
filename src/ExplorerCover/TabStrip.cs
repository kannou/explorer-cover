using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerCover.Core;
using ExplorerCover.Shell;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ExplorerCover;

// タブの並べ替えはマウスキャプチャ、ファイルのドロップはShellで扱う。
internal sealed class TabStrip : Grid, IDisposable
{
    private readonly PaneState state;
    private readonly Action<TabState> close;
    private readonly Action add;
    private readonly Action<string> showDropError;
    private readonly Func<string, ShellFolderDropTarget> createDropTarget;
    private MouseButton? closeButton;
    private readonly StackPanel headers = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<TabState, RadioButton> buttons = [];
    private readonly ScrollViewer scroll;
    private readonly Border marker = new() { Width = 3, Background = Brushes.DodgerBlue, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Visibility = Visibility.Hidden };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private TabState? pressedTab;
    private MouseButton? pressedButton;
    private Point origin;
    private bool dragging;
    private bool moved;
    private bool addOnRelease;
    private int targetIndex;
    private Window? window;
    private bool disposed;
    private readonly Border dropHighlight = new() { BorderBrush = Brushes.DodgerBlue, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Hidden };
    private ShellFolderDropTarget? fileTarget;
    private TabState? fileTab;
    private string? filePath;
    private ComDataObject? fileData;
    private Point filePoint;
    private DragDropKeyStates fileKeys;
    private DragDropEffects fileAllowed;

    public TabStrip(PaneState state, MouseSettings settings, Action<TabState> close, Action add, string label, Action<string> showDropError, Func<string, ShellFolderDropTarget>? createDropTarget = null)
    {
        this.state = state; this.close = close; this.add = add;
        this.showDropError = showDropError;
        this.createDropTarget = createDropTarget ?? ShellFolderDropTarget.Create;
        ApplySettings(settings);
        Background = Brushes.Transparent; ClipToBounds = true; AllowDrop = true;
        scroll = new ScrollViewer { Content = headers, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent, CanContentScroll = false };
        Children.Add(scroll); Children.Add(marker); Children.Add(dropHighlight);
        AutomationProperties.SetAutomationId(this, label + ".tabStrip");
        ((INotifyCollectionChanged)state.Tabs).CollectionChanged += TabsChanged;
        timer.Tick += AutoScroll;
        Loaded += (_, _) => { window = Window.GetWindow(this); if (window != null) window.Deactivated += Deactivated; };
        Unloaded += (_, _) => { if (window != null) window.Deactivated -= Deactivated; window = null; Cancel(); ClearFileDrag(); };
    }

    public void Add(TabState tab, RadioButton button)
    { buttons.Add(tab, button); button.Tag = tab; headers.Children.Add(button); }
    public void ApplySettings(MouseSettings settings)
    {
        Cancel();
        closeButton = settings.CloseTabButton switch
        {
            TabCloseButton.Middle => MouseButton.Middle, TabCloseButton.Right => MouseButton.Right,
            TabCloseButton.XButton1 => MouseButton.XButton1, TabCloseButton.XButton2 => MouseButton.XButton2, _ => null
        };
    }
    public void Reveal(TabState tab)
    {
        // 追加・並べ替え直後は見出しの位置が未確定。レイアウト後にスクロールする。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!disposed && state.SelectedTab == tab && buttons.TryGetValue(tab, out var button)) button.BringIntoView();
        });
    }
    public void Remove(TabState tab)
    {
        if (pressedTab == tab) Cancel();
        if (fileTab == tab) ClearFileDrag();
        if (buttons.Remove(tab, out var button)) headers.Children.Remove(button);
    }
    private void TabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Move) return;
        var button = buttons[(TabState)e.NewItems![0]!];
        headers.Children.Remove(button); headers.Children.Insert(e.NewStartingIndex, button);
    }

    private TabState? HitTab(Point point)
    {
        var hit = InputHitTest(point) as DependencyObject;
        while (hit != null && hit != this)
        {
            if (hit is RadioButton { Tag: TabState tab } && buttons.ContainsKey(tab)) return tab;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }
    private bool InViewport(Point point) => point.X >= 0 && point.X < ActualWidth && point.Y >= 0 && point.Y < scroll.ViewportHeight;

    protected override void OnPreviewDragEnter(DragEventArgs e) { base.OnPreviewDragEnter(e); FileDrag(e); }
    protected override void OnPreviewDragOver(DragEventArgs e) { base.OnPreviewDragOver(e); FileDrag(e); }
    private void FileDrag(DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (disposed || dragging || e.Data is not ComDataObject data) { ClearFileDrag(); return; }
        fileData = data; filePoint = e.GetPosition(this); fileKeys = e.KeyStates; fileAllowed = e.AllowedEffects;
        timer.Start();
        e.Effects = UpdateFileTarget();
    }
    private DragDropEffects UpdateFileTarget()
    {
        dropHighlight.Visibility = Visibility.Hidden;
        var tab = InViewport(filePoint) ? HitTab(filePoint) : null;
        var path = tab?.CurrentPath ?? tab?.InitialPath;
        try
        {
            DragDropEffects effect;
            if (fileTab != tab || filePath != path)
            {
                fileTarget?.Dispose(); fileTarget = null;
                fileTab = tab; filePath = path;
                if (tab == null || path == null || fileData == null) return DragDropEffects.None;
                fileTarget = createDropTarget(path);
                effect = fileTarget.Enter(fileData, fileKeys, PointToScreen(filePoint), fileAllowed);
            }
            else
                effect = fileTarget?.Over(fileKeys, PointToScreen(filePoint), fileAllowed) ?? DragDropEffects.None;
            if (effect != DragDropEffects.None && tab != null)
            {
                var button = buttons[tab]; var position = button.TranslatePoint(new Point(), this);
                dropHighlight.Margin = new Thickness(position.X, position.Y, 0, 0);
                dropHighlight.Width = button.ActualWidth; dropHighlight.Height = button.ActualHeight;
                dropHighlight.Visibility = Visibility.Visible;
            }
            return effect;
        }
        catch (Exception ex) when (IsDropError(ex))
        {
            fileTarget?.Dispose(); fileTarget = null;
            ReportDropError(ex);
            return DragDropEffects.None;
        }
    }
    protected override void OnPreviewDragLeave(DragEventArgs e)
    {
        base.OnPreviewDragLeave(e);
        ClearFileDrag();
        e.Handled = true;
    }
    protected override void OnPreviewDrop(DragEventArgs e)
    {
        base.OnPreviewDrop(e);
        FileDrag(e);
        try
        {
            if (e.Effects != DragDropEffects.None && fileTarget != null && fileData != null)
            {
                e.Effects = fileTarget.Drop(fileData, e.KeyStates, PointToScreen(filePoint), e.AllowedEffects);
                DiagnosticLog.Write($"Tab file drop: {filePath}; effect={e.Effects}");
            }
        }
        catch (Exception ex) when (IsDropError(ex)) { e.Effects = DragDropEffects.None; ReportDropError(ex); }
        finally { ClearFileDrag(); }
    }
    private static bool IsDropError(Exception ex) => ex is COMException or ArgumentException or IOException or UnauthorizedAccessException;
    private void ReportDropError(Exception ex)
    {
        DiagnosticLog.Write("Tab file drop failed: " + ex.Message);
        showDropError("ドロップできません: " + ex.Message);
    }
    private void ClearFileDrag()
    {
        fileTarget?.Dispose(); fileTarget = null; fileTab = null; filePath = null; fileData = null;
        dropHighlight.Visibility = Visibility.Hidden;
        if (!dragging) timer.Stop();
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (pressedButton != null) { Cancel(); e.Handled = true; return; }
        var point = e.GetPosition(this);
        if (!InViewport(point)) return;
        var tab = HitTab(point);
        if (tab == null && (e.ChangedButton != MouseButton.Left || e.ClickCount != 2)) return;
        if (tab != null && e.ChangedButton != MouseButton.Left && e.ChangedButton != closeButton) return;
        pressedTab = tab; pressedButton = e.ChangedButton; origin = point;
        addOnRelease = tab == null && e.ClickCount == 2;
        moved = dragging = false;
        if (tab != null && e.ChangedButton == MouseButton.Left)
        { state.SelectTab(tab); buttons[tab].Focus(); }
        if (!CaptureMouse()) Cancel();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (pressedButton == null) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance)
            moved = true;
        if (moved && pressedButton == MouseButton.Left && pressedTab != null)
        {
            dragging = true; timer.Start(); UpdateTarget(point);
        }
        e.Handled = true;
    }

    private void UpdateTarget(Point point)
    {
        marker.Visibility = InViewport(point) ? Visibility.Visible : Visibility.Hidden;
        if (marker.Visibility != Visibility.Visible || pressedTab == null) return;
        var others = state.Tabs.Where(tab => tab != pressedTab).ToArray();
        targetIndex = 0;
        foreach (var tab in others)
        {
            var button = buttons[tab]; var start = button.TranslatePoint(new Point(), this).X;
            if (point.X < start + button.ActualWidth / 2) break;
            targetIndex++;
        }
        var x = targetIndex < others.Length ? buttons[others[targetIndex]].TranslatePoint(new Point(), this).X :
            others.Length > 0 ? buttons[others[^1]].TranslatePoint(new Point(), this).X + buttons[others[^1]].ActualWidth : 0;
        marker.Margin = new Thickness(Math.Clamp(x, 0, Math.Max(0, ActualWidth - marker.Width)), 0, 0, 0);
        marker.Height = scroll.ViewportHeight;
        marker.VerticalAlignment = VerticalAlignment.Top;
    }

    private void AutoScroll(object? sender, EventArgs e)
    {
        var point = fileData != null ? filePoint : Mouse.GetPosition(this);
        if ((!dragging && fileData == null) || !InViewport(point)) { marker.Visibility = Visibility.Hidden; return; }
        if (point.X < 24) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - 18);
        else if (point.X > ActualWidth - 24) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 18);
        if (fileData != null) UpdateFileTarget(); else UpdateTarget(point);
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        if (e.ChangedButton != pressedButton) return;
        var point = e.GetPosition(this);
        var tab = pressedTab;
        var shouldMove = dragging && tab != null && InViewport(point);
        if (shouldMove) UpdateTarget(point);
        var destination = targetIndex;
        var shouldClose = !moved && tab != null && pressedButton == closeButton && InViewport(point) && HitTab(point) == tab;
        var shouldAdd = !moved && addOnRelease && InViewport(point) && HitTab(point) == null;
        Cancel(); e.Handled = true;
        if (shouldMove)
        {
            DiagnosticLog.Write($"Tab move: {state.Tabs.IndexOf(tab!)} -> {destination}; offset={scroll.HorizontalOffset:0}");
            state.MoveTab(tab!, destination); Reveal(tab!);
        }
        else if (shouldClose) close(tab!);
        else if (shouldAdd) add();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (pressedButton != null && e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { Cancel(); base.OnLostMouseCapture(e); }
    private void Deactivated(object? sender, EventArgs e) => Cancel();
    private void Cancel()
    {
        pressedTab = null; pressedButton = null; dragging = moved = addOnRelease = false;
        timer.Stop(); marker.Visibility = Visibility.Hidden;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
    public void Dispose()
    {
        disposed = true;
        Cancel(); ClearFileDrag(); timer.Tick -= AutoScroll;
        ((INotifyCollectionChanged)state.Tabs).CollectionChanged -= TabsChanged;
        if (window != null) window.Deactivated -= Deactivated;
    }
}
