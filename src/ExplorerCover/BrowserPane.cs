using System.Windows;
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
    public ExplorerHost Browser { get; }
    public PaneState State { get; }
    private readonly TabState tab;
    public TextBox Address { get; } = new() { MinWidth = 80 };
    private readonly TextBlock status = new() { Text = "読み込み中…", TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 5, 8, 5) };
    private readonly Border header;
    public event Action? Activated;

    public BrowserPane(string label, PaneState state, CommandDispatcher commands)
    {
        State = state;
        tab = state.SelectedTab;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new DockPanel { Margin = new Thickness(7) };
        bar.Children.Add(new TextBlock { Text = label, Width = 32, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
        var go = new Button { Content = "移動", Command = new PaneCommand(commands, CommandIds.NavigateAddress, state) };
        DockPanel.SetDock(go, Dock.Right);
        bar.Children.Add(go);
        bar.Children.Add(Address);
        Address.SetBinding(TextBox.TextProperty, new Binding(nameof(TabState.AddressText)) { Source = tab, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        header = new Border { Child = bar, Background = Brushes.WhiteSmoke };
        Children.Add(header);
        Browser = new ExplorerHost(tab.InitialPath);
        SetRow(Browser, 1);
        Children.Add(Browser);
        SetRow(status, 2);
        Children.Add(status);
        Browser.Navigated += current => tab.NavigationSucceeded(current);
        Browser.Error += tab.NavigationFailed;
        Browser.Activated += () => { if (Browser.ContainsNativeFocus) Activated?.Invoke(); };
        Address.GotKeyboardFocus += (_, _) => Activated?.Invoke();
        tab.PropertyChanged += TabChanged;
    }

    private void TabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        status.Text = tab.Error ?? tab.CurrentPath ?? "読み込み中…";
        status.ToolTip = status.Text;
        status.Foreground = tab.Error == null ? Brushes.DimGray : Brushes.Firebrick;
        CommandManager.InvalidateRequerySuggested();
    }
    public void NavigateAddress() { if (Browser.Navigate(tab.AddressText)) Browser.FocusView(); }
    public void FocusFiles() { tab.CancelAddressEdit(); Browser.FocusView(); }
    public void FocusAddress()
    {
        // ネイティブ一覧への移動をWPFが認識していない場合にも、
        // WPF側のHWNDへ戻してからキーボードフォーカスを設定する。
        Keyboard.ClearFocus();
        if (PresentationSource.FromVisual(this) is HwndSource source) Native.SetFocus(source.Handle);
        Address.Focus();
        Address.SelectAll();
    }
    public void SetActive(bool active) => header.Background = active ? new SolidColorBrush(Color.FromRgb(223, 237, 252)) : Brushes.WhiteSmoke;
    public void Dispose() { tab.PropertyChanged -= TabChanged; Browser.Dispose(); }
}
