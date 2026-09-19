using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ExplorerCover.Shell;

namespace ExplorerCover;

public sealed class MainWindow : Window
{
    private readonly BrowserPane left;
    private readonly BrowserPane right;
    private BrowserPane active;

    public MainWindow(string leftPath, string rightPath)
    {
        Title = "explorer_cover — 2ペイン試作";
        Width = 1200; Height = 740; MinWidth = 700; MinHeight = 380;
        FontFamily = new FontFamily("Yu Gothic UI"); FontSize = 13;
        var root = new DockPanel();
        var help = new TextBlock { Text = "Ctrl+L: パス入力  ·  F6: 左右切替  ·  Esc: 一覧に戻る  ｜  試作：タブ・状態保存は未実装", Margin = new Thickness(10, 7, 10, 7), Foreground = Brushes.DimGray };
        DockPanel.SetDock(help, Dock.Bottom); root.Children.Add(help);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
        left = new BrowserPane("左", leftPath); right = new BrowserPane("右", rightPath); active = left;
        grid.Children.Add(left);
        var splitter = new GridSplitter { Width = 7, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.LightGray, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1); grid.Children.Add(splitter);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        root.Children.Add(grid); Content = root;
        left.Activated += () => SetActive(left); right.Activated += () => SetActive(right);
        SetActive(left);
        ComponentDispatcher.ThreadFilterMessage += FilterMessage;
        Closed += (_, _) => { ComponentDispatcher.ThreadFilterMessage -= FilterMessage; left.Dispose(); right.Dispose(); };
    }

    private void SetActive(BrowserPane pane) { active = pane; left.SetActive(pane == left); right.SetActive(pane == right); }

    private void FilterMessage(ref MSG msg, ref bool handled)
    {
        if (handled || !IsActive || (msg.message != 0x100 && msg.message != 0x104)) return;
        var source = left.Browser.ContainsNativeFocus ? left : right.Browser.ContainsNativeFocus ? right : null;
        if (source != null) SetActive(source);
        // Win32の単純なEditはCtrl+Aを実装していないためホスト側で補う。
        // その他のキーは名前変更欄に渡し、ファイル操作へ転送しない。
        if (source != null && Native.IsEditingText())
        {
            if (msg.wParam == 0x41 && Native.GetKeyState(0x11) < 0 && Native.GetKeyState(0x12) >= 0)
            {
                Native.SendMessage(Native.GetFocus(), 0xB1, 0, -1); // EM_SETSEL
                handled = true;
            }
            return;
        }
        bool ctrl = Native.GetKeyState(0x11) < 0, shift = Native.GetKeyState(0x10) < 0, alt = Native.GetKeyState(0x12) < 0;
        if (msg.wParam == 0x4C && ctrl && !alt && !shift)
        { active.FocusAddress(); handled = true; return; }
        if (msg.wParam == 0x75 && !ctrl && !alt && !shift) // F6
        { SetActive(active == left ? right : left); active.Browser.FocusView(); handled = true; return; }
        if (source != null && msg.wParam == 0x09 && !ctrl && !alt)
        { source.FocusAddress(); handled = true; return; }
        if (source != null) handled = source.Browser.TranslateShellKey(ref msg);
    }
}
