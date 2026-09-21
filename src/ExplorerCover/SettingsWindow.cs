using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ExplorerCover.Commands;
using ExplorerCover.Core;

namespace ExplorerCover;

internal sealed class SettingsWindow : Window
{
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly ComboBox closeButton = new() { Width = 210, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox bookmarkButton = new() { Width = 210, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button save = new() { Content = "保存", IsDefault = true, MinWidth = 80 };
    private readonly Action<InputSettings> apply;
    private bool saving;
    private readonly TextBox quickLookPath = new();
    public SettingsWindow(InputSettings current, Action<InputSettings> apply)
    {
        this.apply = apply;
        Title = "設定"; Width = 720; Height = 690; MinWidth = 570; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Yu Gothic UI"); FontSize = 13;
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var reset = new Button { Content = "初期値に戻す" };
        AutomationProperties.SetAutomationId(reset, "Settings.Reset"); reset.Click += (_, _) => Fill(new(new(), new()));
        save.Click += Save; AutomationProperties.SetAutomationId(save, "Settings.Save");
        var cancel = new Button { Content = "キャンセル", IsCancel = true }; AutomationProperties.SetAutomationId(cancel, "Settings.Cancel");
        buttons.Children.Add(reset); buttons.Children.Add(save); buttons.Children.Add(cancel); footer.Children.Add(buttons);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "ショートカット", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"{ProductInfo.Name} バージョン {ProductInfo.Version}（ファイル版 {ProductInfo.FileVersion}）", Foreground = Brushes.DimGray, Margin = new Thickness(0, 3, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Ctrl+T のように入力。複数のキーは / で区切り、空欄で割り当てを解除します。[・] も使用できます。Ctrl・Altなしの [・] は文字編集中には実行しません。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        foreach (var command in CommandCatalog.All)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(230) });
            var scope = command.Scopes == Core.InputScope.Browser ? "一覧のみ" : command.Scopes == Core.InputScope.Address ? "パス欄のみ" : "共通";
            row.Children.Add(new TextBlock { Text = command.Name + "（" + scope + "）", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var input = new TextBox(); AutomationProperties.SetAutomationId(input, "Settings.Key." + command.Id); AutomationProperties.SetName(input, command.Name);
            fields.Add(command.Id, input); input.TextChanged += (_, _) => Validate();
            Grid.SetColumn(input, 1); row.Children.Add(input); panel.Children.Add(row);
        }
        panel.Children.Add(new TextBlock { Text = "タブを閉じるマウスボタン", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var (value, label) in new[] { (TabCloseButton.Middle, "中ボタン"), (TabCloseButton.Right, "右ボタン"), (TabCloseButton.XButton1, "サイドボタン1"), (TabCloseButton.XButton2, "サイドボタン2"), (TabCloseButton.None, "無効") })
            closeButton.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        AutomationProperties.SetAutomationId(closeButton, "Settings.CloseTabButton"); closeButton.SelectionChanged += (_, _) => Validate(); panel.Children.Add(closeButton);
        panel.Children.Add(new TextBlock { Text = "ブックマーク・ドライブを新規タブで開くマウスボタン", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var (value, label) in new[] { (TabCloseButton.Middle, "中ボタン（ホイールクリック）"), (TabCloseButton.Right, "右ボタン"), (TabCloseButton.XButton1, "サイドボタン1"), (TabCloseButton.XButton2, "サイドボタン2"), (TabCloseButton.None, "無効") })
            bookmarkButton.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        AutomationProperties.SetAutomationId(bookmarkButton, "Settings.OpenBookmarkInNewTabButton");
        AutomationProperties.SetName(bookmarkButton, "ブックマーク・ドライブを新規タブで開くマウスボタン");
        bookmarkButton.SelectionChanged += (_, _) => Validate(); panel.Children.Add(bookmarkButton);
        panel.Children.Add(new TextBlock { Text = "現在のペインにタブを追加して選択します。左クリックは現在のタブで開きます。右ボタンに割り当てた場合、ブックマークのメニューはShift+F10で表示できます。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "QuickLookの起動先（空欄で自動検出）", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
        AutomationProperties.SetAutomationId(quickLookPath, "Settings.QuickLookPath"); AutomationProperties.SetName(quickLookPath, "QuickLook.exeの絶対パス");
        quickLookPath.TextChanged += (_, _) => Validate(); panel.Children.Add(quickLookPath);
        panel.Children.Add(new TextBlock { Text = "変更は保存後すぐに反映されます。文字編集中のキーと、右クリックメニュー内の操作はWindowsの設定に従います。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 12, 0, 0) });
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        Fill(current); Closing += (_, e) => { if (saving) e.Cancel = true; };
    }
    private void Fill(InputSettings settings)
    {
        quickLookPath.Text = settings.QuickLook?.ExecutablePath ?? "";
        foreach (var command in CommandCatalog.All) fields[command.Id].Text = string.Join(" / ", settings.Shortcuts.Bindings.Where(b => b.CommandId == command.Id).Select(b => b.Gesture.ToString()));
        closeButton.SelectedItem = closeButton.Items.Cast<ComboBoxItem>().First(i => (TabCloseButton)i.Tag == settings.Mouse.CloseTabButton); Validate();
        bookmarkButton.SelectedItem = bookmarkButton.Items.Cast<ComboBoxItem>().First(i => (TabCloseButton)i.Tag == settings.Mouse.OpenBookmarkInNewTabButton); Validate();
    }
    private InputSettings Read() => new(new ShortcutMap(fields.ToDictionary(pair => pair.Key, pair => pair.Value.Text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))), new((TabCloseButton)((ComboBoxItem)closeButton.SelectedItem).Tag, (TabCloseButton)((ComboBoxItem)bookmarkButton.SelectedItem).Tag),
        QuickLookSettings.FromJson(System.Text.Json.JsonSerializer.Serialize(new { version = 1, executablePath = string.IsNullOrWhiteSpace(quickLookPath.Text) ? null : quickLookPath.Text.Trim() })));
    private void Validate()
    {
        if (closeButton.SelectedItem == null || bookmarkButton.SelectedItem == null) { save.IsEnabled = false; return; }
        try { Read(); error.Text = ""; save.IsEnabled = true; }
        catch (FormatException ex) { error.Text = ex.Message; save.IsEnabled = false; }
    }
    private async void Save(object sender, RoutedEventArgs e)
    {
        InputSettings next;
        try { next = Read(); } catch (FormatException ex) { error.Text = ex.Message; return; }
        saving = true; IsEnabled = false;
        try { await Task.Run(() => InputSettingsFile.Save(next)); apply(next); saving = false; DialogResult = true; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or FormatException or NotSupportedException)
        { error.Text = "設定を保存できません。変更は反映していません: " + ex.Message; }
        finally { saving = false; IsEnabled = true; }
    }
}
