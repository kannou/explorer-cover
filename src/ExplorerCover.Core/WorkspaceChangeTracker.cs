using System.Collections.Specialized;
using System.ComponentModel;

namespace ExplorerCover.Core;

// UIスレッド専用。保存対象だけを監視し、削除された項目への参照は解除する。
public sealed class WorkspaceChangeTracker : IDisposable
{
    private readonly WorkspaceState state;
    private readonly HashSet<TabState> tabs = [];
    private readonly HashSet<BookmarkState> bookmarks = [];
    public event Action? Changed;

    public WorkspaceChangeTracker(WorkspaceState state)
    {
        this.state = state;
        state.PropertyChanged += PropertyChanged;
        state.Sidebar.PropertyChanged += PropertyChanged;
        foreach (var pane in new[] { state.Left, state.Right })
        {
            pane.PropertyChanged += PropertyChanged;
            ((INotifyCollectionChanged)pane.Tabs).CollectionChanged += TabsChanged;
            foreach (var tab in pane.Tabs) AddTab(tab);
        }
        ((INotifyCollectionChanged)state.Sidebar.Bookmarks).CollectionChanged += BookmarksChanged;
        foreach (var bookmark in state.Sidebar.Bookmarks) AddBookmark(bookmark);
    }

    private void PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var persisted = e.PropertyName is null or "" || sender switch
        {
            WorkspaceState => e.PropertyName is nameof(WorkspaceState.ActivePane) or nameof(WorkspaceState.LeftPaneRatio),
            PaneState => e.PropertyName == nameof(PaneState.SelectedTab),
            TabState => e.PropertyName == nameof(TabState.CurrentPath),
            SidebarState => e.PropertyName == nameof(SidebarState.Width),
            BookmarkState => e.PropertyName == nameof(BookmarkState.Name),
            _ => false
        };
        if (persisted) Changed?.Invoke();
    }

    private void AddTab(TabState tab) { if (tabs.Add(tab)) tab.PropertyChanged += PropertyChanged; }
    private void RemoveTab(TabState tab) { if (tabs.Remove(tab)) tab.PropertyChanged -= PropertyChanged; }
    private void AddBookmark(BookmarkState bookmark) { if (bookmarks.Add(bookmark)) bookmark.PropertyChanged += PropertyChanged; }
    private void RemoveBookmark(BookmarkState bookmark) { if (bookmarks.Remove(bookmark)) bookmark.PropertyChanged -= PropertyChanged; }

    private void TabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var tab in tabs.ToArray()) RemoveTab(tab);
            foreach (var tab in state.Left.Tabs.Concat(state.Right.Tabs)) AddTab(tab);
        }
        else if (e.Action != NotifyCollectionChangedAction.Move)
        {
            if (e.OldItems != null) foreach (TabState tab in e.OldItems) RemoveTab(tab);
            if (e.NewItems != null) foreach (TabState tab in e.NewItems) AddTab(tab);
        }
        Changed?.Invoke();
    }

    private void BookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var bookmark in bookmarks.ToArray()) RemoveBookmark(bookmark);
            foreach (var bookmark in state.Sidebar.Bookmarks) AddBookmark(bookmark);
        }
        else if (e.Action != NotifyCollectionChangedAction.Move)
        {
            if (e.OldItems != null) foreach (BookmarkState bookmark in e.OldItems) RemoveBookmark(bookmark);
            if (e.NewItems != null) foreach (BookmarkState bookmark in e.NewItems) AddBookmark(bookmark);
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        state.PropertyChanged -= PropertyChanged;
        state.Sidebar.PropertyChanged -= PropertyChanged;
        foreach (var pane in new[] { state.Left, state.Right })
        {
            pane.PropertyChanged -= PropertyChanged;
            ((INotifyCollectionChanged)pane.Tabs).CollectionChanged -= TabsChanged;
        }
        ((INotifyCollectionChanged)state.Sidebar.Bookmarks).CollectionChanged -= BookmarksChanged;
        foreach (var tab in tabs) tab.PropertyChanged -= PropertyChanged;
        foreach (var bookmark in bookmarks) bookmark.PropertyChanged -= PropertyChanged;
        tabs.Clear(); bookmarks.Clear();
    }
}
