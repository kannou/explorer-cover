using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerCover.Core;
using ExplorerCover.Shell;

namespace ExplorerCover;

public sealed class SidebarView : DockPanel, IDisposable
{
    private readonly WorkspaceState workspace;
    private readonly Action<string> navigate;
    private readonly StackPanel bookmarks = new();
    private readonly StackPanel drives = new();
    private readonly ShellIcons icons = new();
    private bool disposed;
    private readonly TextBlock driveError = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, (Button Button, TextBlock Name, TextBlock Capacity, ProgressBar Bar, Image Icon)> driveRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DriveMonitor monitor;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };

    public SidebarView(WorkspaceState workspace, Action<string> navigate, bool initializeBookmarks = true)
    {
        this.workspace = workspace; this.navigate = navigate;
        Background = new SolidColorBrush(Color.FromRgb(248, 249, 251));
        var body = new StackPanel { Margin = new Thickness(8, 8, 8, 12) };
        body.Children.Add(SectionHeader("ドライブ", "↻", "ドライブを更新", "Sidebar.RefreshDrives", () => _ = monitor!.RefreshAsync()));
        body.Children.Add(driveError); body.Children.Add(drives);
        body.Children.Add(SectionHeader("ブックマーク", "＋", "現在地を登録", "Sidebar.AddBookmark", AddCurrent, 18));
        body.Children.Add(bookmarks);
        Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        monitor = new(() => Task.Run(() => DriveInfo.GetDrives().Select(d => d.Name).ToArray()), path => Task.Run(() => ReadDrive(path)));
        monitor.PathsChanged += ReconcileDrives;
        monitor.Updated += UpdateDrive;
        monitor.Failed += message => { driveError.Text = "ドライブ一覧を取得できません: " + message; driveError.Visibility = Visibility.Visible; };
        ((System.Collections.Specialized.INotifyCollectionChanged)workspace.Sidebar.Bookmarks).CollectionChanged += BookmarksChanged;
        if (initializeBookmarks && workspace.Sidebar.Bookmarks.Count == 0)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            workspace.Sidebar.Add(Path.GetFileName(home.TrimEnd('\\')), home);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(desktop)) workspace.Sidebar.Add(Path.GetFileName(desktop.TrimEnd('\\')), desktop);
        }
        RenderBookmarks();
        Loaded += Start;
        timer.Tick += Refresh;
    }

    private static Button MakeButton(string text, string id, Action action)
    {
        var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(5, 6, 5, 6), Margin = new Thickness(0, 1, 0, 1) };
        button.SetResourceReference(StyleProperty, "SidebarButton");
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => action();
        return button;
    }
    private static DockPanel SectionHeader(string title, string symbol, string hint, string id, Action action, double top = 0)
    {
        var header = new DockPanel { Margin = new Thickness(4, top, 0, 4) };
        var button = MakeButton(symbol, id, action);
        button.Width = 28; button.Height = 28; button.Padding = new Thickness(0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center; button.FontSize = 18;
        button.ToolTip = hint; AutomationProperties.SetName(button, hint);
        DockPanel.SetDock(button, Dock.Right); header.Children.Add(button);
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 101)), VerticalAlignment = VerticalAlignment.Center });
        return header;
    }
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
            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); content.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new Image { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left };
            content.Children.Add(icon);
            var label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var name = new System.Windows.Documents.Run();
            name.SetBinding(System.Windows.Documents.Run.TextProperty, new Binding(nameof(BookmarkState.Name)) { Source = bookmark, Mode = BindingMode.OneWay });
            label.Inlines.Add(name);
            label.Inlines.Add(new System.Windows.Documents.Run($" ({bookmark.Path})") { Foreground = Brushes.DimGray });
            Grid.SetColumn(label, 1); content.Children.Add(label);
            button.Content = content;
            button.SetBinding(ToolTipProperty, new Binding(nameof(BookmarkState.DisplayText)) { Source = bookmark });
            button.SetBinding(AutomationProperties.HelpTextProperty, new Binding(nameof(BookmarkState.DisplayText)) { Source = bookmark });
            button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(BookmarkState.Name)) { Source = bookmark });
            _ = LoadIconAsync(icon, bookmark.Path);
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
            // IsReadyはアクセス拒否もfalseにするため、実際の取得エラーで未接続と区別する。
            var total = drive.TotalSize;
            var free = drive.AvailableFreeSpace;
            var name = drive.VolumeLabel;
            return new(path, string.IsNullOrEmpty(name) ? path : $"{name}({path})", total, free);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 3 or 15 or 21 or 53 or 67 or 1167)
        { return new(path, path, null, null, null, DriveAvailability.Unavailable); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { return new(path, path, null, null, "容量を取得できません", DriveAvailability.Unknown); }
    }
    private void ReconcileDrives(IReadOnlyCollection<string> paths)
    {
        driveError.Text = "";
        driveError.Visibility = Visibility.Collapsed;
        if (driveRows.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(paths)) return;
        foreach (var removed in driveRows.Keys.Except(paths, StringComparer.OrdinalIgnoreCase).ToArray())
        { drives.Children.Remove(driveRows[removed].Button); driveRows.Remove(removed); }
        foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (driveRows.ContainsKey(path)) continue;
            var name = new TextBlock { Text = path, TextTrimming = TextTrimming.CharacterEllipsis };
            var capacity = new TextBlock { FontSize = 11, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Margin = new Thickness(24, 5, 0, 0), BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(87, 148, 199)), Background = new SolidColorBrush(Color.FromRgb(222, 227, 234)) };
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left };
            line.Children.Add(icon); Grid.SetColumn(name, 1); line.Children.Add(name); Grid.SetColumn(capacity, 2); line.Children.Add(capacity);
            name.VerticalAlignment = VerticalAlignment.Center;
            var content = new StackPanel(); content.Children.Add(line); content.Children.Add(bar);
            var button = MakeButton("", "Sidebar.Drive." + path, () => navigate(path));
            button.Content = content; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.ToolTip = path;
            AutomationProperties.SetName(button, path);
            button.Visibility = Visibility.Collapsed;
            driveRows.Add(path, (button, name, capacity, bar, icon));
        }
        drives.Children.Clear();
        foreach (var row in driveRows.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)) drives.Children.Add(row.Value.Button);
    }
    private void UpdateDrive(DriveSnapshot drive)
    {
        if (!driveRows.TryGetValue(drive.Path, out var row)) return;
        if (drive.Availability == DriveAvailability.Unavailable) { row.Button.Visibility = Visibility.Collapsed; return; }
        if (drive.Availability == DriveAvailability.Ready)
        {
            row.Button.Visibility = Visibility.Visible;
            row.Name.Text = drive.Name;
            if (row.Icon.Tag == null) { row.Icon.Tag = drive.Path; _ = LoadIconAsync(row.Icon, drive.Path); }
        }
        row.Capacity.Text = drive.TotalBytes is long total && drive.FreeBytes is long free ? $"{FormatBytes(free)}/{FormatBytes(total)}" : "—";
        row.Bar.Value = drive.UsedPercent;
        row.Bar.Visibility = drive.Error == null && drive.TotalBytes != null ? Visibility.Visible : Visibility.Hidden;
        row.Button.ToolTip = row.Name.Text + "\n" + (drive.Error ?? $"空き容量 / 総容量：{row.Capacity.Text}\n使用率 {drive.UsedPercent:F0}%");
        AutomationProperties.SetName(row.Button, row.Name.Text + " " + row.Capacity.Text);
        AutomationProperties.SetHelpText(row.Button, row.Button.ToolTip.ToString());
    }
    private static string FormatBytes(long bytes)
    {
        return $"{bytes / 1073741824.0:F1}GiB";
    }
    private async Task LoadIconAsync(Image image, string path)
    {
        if (image.Source == null)
        {
            var fallback = await icons.GetAsync(null);
            if (disposed) return;
            image.Source = fallback;
        }
        var actual = await icons.GetAsync(path);
        if (!disposed && actual != null) image.Source = actual;
    }
    private void Start(object sender, RoutedEventArgs e) { timer.Start(); _ = monitor.RefreshAsync(); }
    private void Refresh(object? sender, EventArgs e) => _ = monitor.RefreshAsync();
    public void Dispose()
    {
        Loaded -= Start; timer.Stop(); timer.Tick -= Refresh; monitor.Dispose();
        disposed = true; icons.Dispose();
        ((System.Collections.Specialized.INotifyCollectionChanged)workspace.Sidebar.Bookmarks).CollectionChanged -= BookmarksChanged;
    }
}
