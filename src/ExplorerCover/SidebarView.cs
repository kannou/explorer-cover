using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerCover.Core;

namespace ExplorerCover;

public sealed class SidebarView : DockPanel, IDisposable
{
    private readonly WorkspaceState workspace;
    private readonly Action<string> navigate;
    private readonly StackPanel bookmarks = new();
    private readonly StackPanel drives = new();
    private readonly TextBlock target = new() { Margin = new Thickness(8), FontWeight = FontWeights.SemiBold };
    private readonly TextBlock driveError = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
    private readonly Dictionary<string, (Button Button, TextBlock Name, TextBlock Capacity, ProgressBar Bar)> driveRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DriveMonitor monitor;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };

    public SidebarView(WorkspaceState workspace, Action<string> navigate)
    {
        this.workspace = workspace; this.navigate = navigate;
        Background = Brushes.WhiteSmoke;
        DockPanel.SetDock(target, Dock.Top); Children.Add(target);
        AutomationProperties.SetAutomationId(target, "Sidebar.Target");
        target.SetBinding(ToolTipProperty, new Binding("ActivePane.SelectedTab.CurrentPath") { Source = workspace });
        var body = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
        body.Children.Add(new TextBlock { Text = "ブックマーク", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 5) });
        body.Children.Add(MakeButton("＋ 現在地を登録", "Sidebar.AddBookmark", AddCurrent));
        body.Children.Add(bookmarks);
        body.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 8) });
        body.Children.Add(new TextBlock { Text = "ドライブ", FontWeight = FontWeights.Bold });
        body.Children.Add(MakeButton("更新", "Sidebar.RefreshDrives", () => _ = monitor!.RefreshAsync()));
        body.Children.Add(driveError); body.Children.Add(drives);
        Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        monitor = new(() => Task.Run(() => DriveInfo.GetDrives().Select(d => d.Name).ToArray()), path => Task.Run(() => ReadDrive(path)));
        monitor.PathsChanged += ReconcileDrives;
        monitor.Updated += UpdateDrive;
        monitor.Failed += message => driveError.Text = "ドライブ一覧を取得できません: " + message;
        workspace.PropertyChanged += WorkspaceChanged;
        ((System.Collections.Specialized.INotifyCollectionChanged)workspace.Sidebar.Bookmarks).CollectionChanged += BookmarksChanged;
        if (workspace.Sidebar.Bookmarks.Count == 0)
        {
            workspace.Sidebar.Add("ホーム", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(desktop)) workspace.Sidebar.Add("デスクトップ", desktop);
        }
        RenderBookmarks(); UpdateTarget();
        Loaded += Start;
        timer.Tick += Refresh;
    }

    private static Button MakeButton(string text, string id, Action action)
    {
        var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 3, 0, 3) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => action();
        return button;
    }
    private void WorkspaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(WorkspaceState.ActivePane)) UpdateTarget(); }
    private void UpdateTarget() => target.Text = "移動先：" + (workspace.ActivePane == workspace.Left ? "左ペイン" : "右ペイン");
    private void BookmarksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RenderBookmarks();
    private void AddCurrent()
    {
        if (workspace.ActivePane.SelectedTab.CurrentPath is not string path) return;
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        workspace.Sidebar.Add(string.IsNullOrEmpty(name) ? path : name, path);
    }
    private void RenderBookmarks()
    {
        bookmarks.Children.Clear();
        foreach (var bookmark in workspace.Sidebar.Bookmarks)
        {
            var button = MakeButton("", "Sidebar.Bookmark." + bookmark.Id, () => navigate(bookmark.Path));
            var label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            label.SetBinding(TextBlock.TextProperty, new Binding(nameof(BookmarkState.Name)) { Source = bookmark });
            button.Content = label; button.ToolTip = bookmark.Path;
            button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(BookmarkState.Name)) { Source = bookmark });
            var menu = new ContextMenu();
            var rename = new MenuItem { Header = "名前を変更" };
            rename.Click += (_, _) => Rename(bookmark);
            var remove = new MenuItem { Header = "ブックマークを削除" };
            remove.Click += (_, _) => workspace.Sidebar.Remove(bookmark);
            menu.Items.Add(rename); menu.Items.Add(remove); button.ContextMenu = menu;
            bookmarks.Children.Add(button);
        }
    }
    private void Rename(BookmarkState bookmark)
    {
        var dialog = new Window { Title = "ブックマーク名を変更", Owner = Window.GetWindow(this), Width = 360, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(16) };
        var input = new TextBox { Text = bookmark.Name, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(input, "Bookmark.Name");
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = MakeButton("変更", "Bookmark.Save", () => { if (!string.IsNullOrWhiteSpace(input.Text)) { bookmark.Rename(input.Text); dialog.DialogResult = true; } });
        save.IsDefault = true;
        var cancel = new Button { Content = "キャンセル", IsCancel = true, Margin = new Thickness(8, 3, 0, 3), Padding = new Thickness(6) };
        input.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        buttons.Children.Add(save); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        dialog.ShowDialog();
    }
    private static DriveSnapshot ReadDrive(string path)
    {
        try
        {
            var drive = new DriveInfo(path);
            if (!drive.IsReady) return new(path, path, null, null, "未接続・メディアなし");
            var name = drive.VolumeLabel;
            return new(path, string.IsNullOrEmpty(name) ? path : $"{name} ({path})", drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { return new(path, path, null, null, "容量を取得できません"); }
    }
    private void ReconcileDrives(IReadOnlyCollection<string> paths)
    {
        driveError.Text = "";
        if (driveRows.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(paths)) return;
        foreach (var removed in driveRows.Keys.Except(paths, StringComparer.OrdinalIgnoreCase).ToArray())
        { drives.Children.Remove(driveRows[removed].Button); driveRows.Remove(removed); }
        foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (driveRows.ContainsKey(path)) continue;
            var name = new TextBlock { Text = path, TextTrimming = TextTrimming.CharacterEllipsis };
            var capacity = new TextBlock { Text = "取得中…", FontSize = 11, TextWrapping = TextWrapping.Wrap };
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, Margin = new Thickness(0, 5, 0, 3) };
            var content = new StackPanel(); content.Children.Add(name); content.Children.Add(bar); content.Children.Add(capacity);
            var button = MakeButton("", "Sidebar.Drive." + path, () => navigate(path));
            button.Content = content; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.ToolTip = path;
            AutomationProperties.SetName(button, path);
            driveRows.Add(path, (button, name, capacity, bar));
        }
        drives.Children.Clear();
        foreach (var row in driveRows.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)) drives.Children.Add(row.Value.Button);
    }
    private void UpdateDrive(DriveSnapshot drive)
    {
        if (!driveRows.TryGetValue(drive.Path, out var row)) return;
        row.Name.Text = drive.Name;
        row.Capacity.Text = drive.Error ?? (drive.TotalBytes is long total && drive.FreeBytes is long free ? $"空き {FormatBytes(free)} / {FormatBytes(total)}\n使用率 {drive.UsedPercent:F0}%" : "容量を取得できません");
        row.Bar.Value = drive.UsedPercent;
        row.Bar.Visibility = drive.Error == null ? Visibility.Visible : Visibility.Collapsed;
        row.Button.ToolTip = drive.Path + "\n" + row.Capacity.Text;
        AutomationProperties.SetName(row.Button, drive.Name + " " + row.Capacity.Text);
    }
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes; int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
    private void Start(object sender, RoutedEventArgs e) { timer.Start(); _ = monitor.RefreshAsync(); }
    private void Refresh(object? sender, EventArgs e) => _ = monitor.RefreshAsync();
    public void Dispose()
    {
        Loaded -= Start; timer.Stop(); timer.Tick -= Refresh; monitor.Dispose();
        workspace.PropertyChanged -= WorkspaceChanged;
        ((System.Collections.Specialized.INotifyCollectionChanged)workspace.Sidebar.Bookmarks).CollectionChanged -= BookmarksChanged;
    }
}
