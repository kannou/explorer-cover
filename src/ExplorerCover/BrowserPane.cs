using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ExplorerCover.Shell;

namespace ExplorerCover;

public sealed class BrowserPane : Grid, IDisposable
{
    public ExplorerHost Browser { get; }
    public TextBox Address { get; } = new() { MinWidth = 80 };
    private readonly TextBlock status = new() { Text = "読み込み中…", TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 5, 8, 5) };
    private readonly Border header;
    public event Action? Activated;

    public BrowserPane(string label, string path)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new DockPanel { Margin = new Thickness(7) };
        bar.Children.Add(new TextBlock { Text = label, Width = 32, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
        var go = new Button { Content = "移動" };
        DockPanel.SetDock(go, Dock.Right);
        go.Click += (_, _) => Navigate();
        bar.Children.Add(go);
        bar.Children.Add(Address);
        Address.Text = path;
        header = new Border { Child = bar, Background = Brushes.WhiteSmoke };
        Children.Add(header);
        Browser = new ExplorerHost(path);
        SetRow(Browser, 1);
        Children.Add(Browser);
        SetRow(status, 2);
        Children.Add(status);
        Browser.Navigated += current => { Address.Text = current; status.Text = current; status.Foreground = Brushes.DimGray; };
        Browser.Error += message => { status.Text = message; status.ToolTip = message; status.Foreground = Brushes.Firebrick; };
        Browser.Activated += () => Activated?.Invoke();
        Address.GotKeyboardFocus += (_, _) => Activated?.Invoke();
        Address.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Navigate(); e.Handled = true; }
            if (e.Key == Key.Escape) { Address.Text = Browser.CurrentPath; Browser.FocusView(); e.Handled = true; }
        };
    }

    private void Navigate() { if (Browser.Navigate(Address.Text)) Browser.FocusView(); }
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
    public void Dispose() => Browser.Dispose();
}
