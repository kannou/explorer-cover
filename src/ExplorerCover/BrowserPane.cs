using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using ExplorerCover.Core;
using ExplorerCover.Commands;
using ExplorerCover.Shell;

namespace ExplorerCover;

public sealed class BrowserPane : Grid, IDisposable
{
    private sealed record TabView(ExplorerHost Host, TabNavigation Navigation, RadioButton Header);
    private readonly Dictionary<TabState, TabView> views = [];
    private readonly Grid browsers = new() { Margin = new Thickness(3, 0, 3, 0) };
    private readonly TabStrip tabs;
    private readonly CommandDispatcher commands;
    private readonly string label;
    private TabState? focusAfterNavigation;
    private bool disposed;
    private bool active;
    private readonly string initialPath;
    public ExplorerHost Browser => views[State.SelectedTab].Host;
    public PaneState State { get; }
    public bool CanNavigate => !Browser.IsNavigating;
    public TextBox Address { get; } = new() { MinWidth = 40 };
    private readonly TextBlock status = new() { Text = "読み込み中…", TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 5, 8, 5) };
    private readonly Border header;
    private string? operationMessage;
    public void ShowOperationMessage(string? message) { operationMessage = message; UpdateStatus(); }
    public event Action? Activated;

    public BrowserPane(string label, PaneState state, CommandDispatcher commands, MouseSettings mouseSettings)
    {
        State = state; this.commands = commands; this.label = label;
        initialPath = state.SelectedTab.InitialPath;
        tabs = new TabStrip(state, mouseSettings, CloseTab, () => commands.Execute(CommandIds.NewTab, state), label);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tabBar = new DockPanel { Margin = new Thickness(7, 5, 7, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button("＋", "新しいタブ", CommandIds.NewTab));
        var duplicate = Button("", "タブを複製", CommandIds.DuplicateTab);
        duplicate.Content = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 5,5 L 5,1 15,1 15,11 11,11 M 1,5 L 11,5 11,15 1,15 Z"),
            StrokeThickness = 1, Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        ((System.Windows.Shapes.Path)duplicate.Content).SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = duplicate });
        actions.Children.Add(duplicate);
        actions.Children.Add(Button("×", "タブを閉じる", CommandIds.CloseTab));
        DockPanel.SetDock(actions, Dock.Right); tabBar.Children.Add(actions);
        tabBar.Children.Add(tabs);
        Children.Add(tabBar);
        var bar = new DockPanel { Margin = new Thickness(7) };
        bar.Children.Add(Button("←", "戻る", CommandIds.Back));
        bar.Children.Add(Button("→", "進む", CommandIds.Forward));
        bar.Children.Add(Button("↑", "ひとつ上へ", CommandIds.Parent));
        var go = Button("→", "移動", CommandIds.NavigateAddress);
        DockPanel.SetDock(go, Dock.Right); bar.Children.Add(go);
        bar.Children.Add(Address);
        AutomationProperties.SetAutomationId(Address, label + "Address");
        header = new Border { Child = bar, Background = Brushes.WhiteSmoke };
        SetRow(header, 1); Children.Add(header);
        SetRow(browsers, 2); Children.Add(browsers);
        SetRow(status, 3); Children.Add(status);
        AutomationProperties.SetAutomationId(status, label + "Status");
        AddView(state.SelectedTab);
        state.PropertyChanged += PaneChanged;
        Address.GotKeyboardFocus += (_, _) => Activated?.Invoke();
        PreviewMouseDown += (_, _) => Activated?.Invoke();
        GotKeyboardFocus += (_, _) => Activated?.Invoke();
        SelectView();
    }

    private Button Button(string text, string name, string command)
    {
        var button = new Button { Content = text, ToolTip = name, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 3, 0), Command = new PaneCommand(commands, command, State) };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, label + "." + command);
        return button;
    }

    private void AddView(TabState tab)
    {
        var host = new ExplorerHost(tab.InitialPath) { Visibility = Visibility.Hidden };
        var navigation = new TabNavigation(tab);
        var tabHeader = new RadioButton { GroupName = State.Id.ToString(), Padding = new Thickness(5), Margin = new Thickness(0), MinWidth = 60, MaxWidth = 180, VerticalAlignment = VerticalAlignment.Center };
        tabHeader.SetResourceReference(StyleProperty, "TabHeader");
        tabHeader.Checked += (_, _) => { if (!disposed) { State.SelectTab(tab); Activated?.Invoke(); } };
        AutomationProperties.SetAutomationId(tabHeader, label + ".tab." + tab.Id);
        views.Add(tab, new(host, navigation, tabHeader));
        tabs.Add(tab, tabHeader); browsers.Children.Add(host);
        host.Navigated += path =>
        {
            if (disposed || !views.ContainsKey(tab)) return;
            navigation.Complete(path);
            if (focusAfterNavigation == tab && State.SelectedTab == tab && active && Window.GetWindow(this)?.IsActive == true)
            { focusAfterNavigation = null; host.FocusView(); }
        };
        host.Error += message => { if (!disposed && views.ContainsKey(tab)) { if (focusAfterNavigation == tab) focusAfterNavigation = null; navigation.Fail(message); } };
        host.NavigationStateChanged += CommandManager.InvalidateRequerySuggested;
        host.Activated += () => { if (!disposed && State.SelectedTab == tab && host.ContainsNativeFocus) Activated?.Invoke(); };
        tab.PropertyChanged += TabChanged;
        UpdateTabHeader(tab);
    }

    private void PaneChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PaneState.SelectedTab)) SelectView(); }

    private void SelectView()
    {
        operationMessage = null;
        focusAfterNavigation = null;
        if (views.Any(v => v.Key != State.SelectedTab && v.Value.Host.ContainsNativeFocus)) FocusAddress();
        foreach (var (tab, view) in views)
        {
            view.Host.Visibility = tab == State.SelectedTab ? Visibility.Visible : Visibility.Hidden;
            view.Header.IsChecked = tab == State.SelectedTab;
        }
        Address.SetBinding(TextBox.TextProperty, new Binding(nameof(TabState.AddressText)) { Source = State.SelectedTab, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        tabs.Reveal(State.SelectedTab);
        UpdateStatus();
    }

    private void UpdateTabHeader(TabState tab)
    {
        var path = tab.CurrentPath ?? tab.InitialPath;
        var title = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(title)) title = path;
        var tabHeader = views[tab].Header;
        tabHeader.Content = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis };
        tabHeader.ToolTip = path;
        AutomationProperties.SetName(tabHeader, title);
    }

    private void TabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TabState tab) return;
        if (tab == State.SelectedTab) operationMessage = null;
        if (e.PropertyName == nameof(TabState.CurrentPath)) UpdateTabHeader(tab);
        if (tab == State.SelectedTab) UpdateStatus();
    }
    private void UpdateStatus()
    {
        var tab = State.SelectedTab;
        status.Text = operationMessage ?? tab.Error ?? tab.CurrentPath ?? "読み込み中…";
        status.ToolTip = status.Text;
        status.Foreground = tab.Error == null && operationMessage == null ? Brushes.DimGray : Brushes.Firebrick;
        CommandManager.InvalidateRequerySuggested();
    }

    public void AddTab(bool duplicate)
    {
        var path = duplicate ? State.SelectedTab.CurrentPath ?? State.SelectedTab.InitialPath : initialPath;
        var tab = State.AddTab(path);
        AddView(tab); State.SelectTab(tab);
        focusAfterNavigation = tab;
        FocusAddress();
    }
    public void CloseTab() => CloseTab(State.SelectedTab);
    private void CloseTab(TabState tab)
    {
        var selected = tab == State.SelectedTab;
        if (!State.CloseTab(tab)) return;
        var view = views[tab]; views.Remove(tab);
        tab.PropertyChanged -= TabChanged;
        tabs.Remove(tab); view.Host.Dispose(); browsers.Children.Remove(view.Host);
        if (selected) FocusFiles();
        UpdateStatus();
    }
    public void CycleTab(int offset)
    {
        State.SelectTab(State.Tabs[(State.Tabs.IndexOf(State.SelectedTab) + offset + State.Tabs.Count) % State.Tabs.Count]);
        FocusFiles();
    }
    public void NavigateAddress() => Navigate(State.SelectedTab.AddressText);
    public void Navigate(string path)
    {
        if (!CanNavigate) return;
        focusAfterNavigation = State.SelectedTab;
        if (!Browser.Navigate(path)) focusAfterNavigation = null;
    }
    public void NavigateHistory(int offset)
    {
        if (!CanNavigate) return;
        var path = views[State.SelectedTab].Navigation.BeginHistory(offset);
        if (path != null) Navigate(path);
    }
    public string? ParentPath
    {
        get
        {
            try { return State.SelectedTab.CurrentPath is string path && Path.IsPathFullyQualified(path) ? Directory.GetParent(path)?.FullName : null; }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
        }
    }
    public void NavigateParent() { if (ParentPath is string path) Navigate(path); }
    public void FocusFiles()
    {
        State.SelectedTab.CancelAddressEdit();
        if (!Browser.FocusView()) FocusAddress();
    }
    public void FocusAddress()
    {
        Keyboard.ClearFocus();
        if (PresentationSource.FromVisual(this) is HwndSource source) Native.SetFocus(source.Handle);
        Address.Focus(); Address.SelectAll();
    }
    public void SetActive(bool active)
    {
        this.active = active;
        if (!active) focusAfterNavigation = null;
        Background = active ? new SolidColorBrush(Color.FromRgb(223, 237, 252)) : new SolidColorBrush(Color.FromRgb(238, 240, 243));
        header.Background = Background;
        Address.Background = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(245, 246, 248));
        Resources["TabSelectionBackground"] = active ? new SolidColorBrush(Color.FromRgb(223, 237, 252)) : new SolidColorBrush(Color.FromRgb(221, 225, 230));
        Resources["TabSelectionBorder"] = active ? new SolidColorBrush(Color.FromRgb(107, 159, 211)) : Brushes.DarkGray;
    }
    public void Dispose()
    {
        disposed = true;
        tabs.Dispose();
        State.PropertyChanged -= PaneChanged;
        foreach (var (tab, view) in views) { tab.PropertyChanged -= TabChanged; view.Host.Dispose(); }
        views.Clear();
    }
}
