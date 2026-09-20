using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerCover.Core;

namespace ExplorerCover;

// マウス操作はタブバー内で完結させ、シェルのファイルDrag & Dropに流さない。
internal sealed class TabStrip : Grid, IDisposable
{
    private readonly PaneState state;
    private readonly Action<TabState> close;
    private readonly Action add;
    private readonly MouseButton? closeButton;
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

    public TabStrip(PaneState state, MouseSettings settings, Action<TabState> close, Action add, string label)
    {
        this.state = state; this.close = close; this.add = add;
        closeButton = settings.CloseTabButton switch
        {
            TabCloseButton.Middle => MouseButton.Middle, TabCloseButton.Right => MouseButton.Right,
            TabCloseButton.XButton1 => MouseButton.XButton1, TabCloseButton.XButton2 => MouseButton.XButton2, _ => null
        };
        Background = Brushes.Transparent; ClipToBounds = true;
        scroll = new ScrollViewer { Content = headers, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent, CanContentScroll = false };
        Children.Add(scroll); Children.Add(marker);
        AutomationProperties.SetAutomationId(this, label + ".tabStrip");
        ((INotifyCollectionChanged)state.Tabs).CollectionChanged += TabsChanged;
        timer.Tick += AutoScroll;
        Loaded += (_, _) => { window = Window.GetWindow(this); if (window != null) window.Deactivated += Deactivated; };
        Unloaded += (_, _) => { if (window != null) window.Deactivated -= Deactivated; window = null; Cancel(); };
    }

    public void Add(TabState tab, RadioButton button)
    { buttons.Add(tab, button); button.Tag = tab; headers.Children.Add(button); }
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
        var point = Mouse.GetPosition(this);
        if (!dragging || !InViewport(point)) { marker.Visibility = Visibility.Hidden; return; }
        if (point.X < 24) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - 18);
        else if (point.X > ActualWidth - 24) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 18);
        UpdateTarget(point);
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
        Cancel(); timer.Tick -= AutoScroll;
        ((INotifyCollectionChanged)state.Tabs).CollectionChanged -= TabsChanged;
        if (window != null) window.Deactivated -= Deactivated;
    }
}
