using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExplorerCover.Core;

public abstract class ObservableState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
}

// このモデルは成功した移動だけを記録する。シェルや画面への参照を保持しない。
public sealed class NavigationHistory
{
    private readonly List<string> entries = [];
    public IReadOnlyList<string> Entries { get; }
    public int Index { get; private set; } = -1;
    public string? Current => Index < 0 ? null : entries[Index];
    public bool CanGoBack => Index > 0;
    public bool CanGoForward => Index >= 0 && Index < entries.Count - 1;
    public NavigationHistory() => Entries = entries.AsReadOnly();

    // historyIndexは、その履歴への移動がシェルで成功した後にのみ渡す。
    public void Commit(string path, int? historyIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (historyIndex is int target)
        {
            if (target < 0 || target >= entries.Count || !SameLocation(entries[target], path))
                throw new ArgumentException("移動先が履歴と一致しません。", nameof(historyIndex));
            Index = target;
            return;
        }
        if (Current is string current && SameLocation(current, path)) return;
        entries.RemoveRange(Index + 1, entries.Count - Index - 1);
        entries.Add(path);
        Index = entries.Count - 1;
    }

    public static bool SameLocation(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}

public sealed class TabState : ObservableState
{
    public Guid Id { get; } = Guid.NewGuid();
    public string InitialPath { get; }
    public NavigationHistory History { get; } = new();
    public string? CurrentPath => History.Current;
    private string addressText;
    public string AddressText { get => addressText; set => Set(ref addressText, value); }
    private string? error;
    public string? Error { get => error; private set => Set(ref error, value); }

    public TabState(string initialPath) { InitialPath = initialPath; addressText = initialPath; }
    public void NavigationSucceeded(string path, int? historyIndex = null)
    {
        History.Commit(path, historyIndex);
        AddressText = path;
        Error = null;
        Notify(nameof(CurrentPath));
        Notify(nameof(History));
    }
    public void NavigationFailed(string message) => Error = message;
    public void CancelAddressEdit() => AddressText = CurrentPath ?? InitialPath;
}

// 1タブの移動要求と完了通知を結び付ける。履歴のカーソルは成功時だけ確定する。
public sealed class TabNavigation(TabState tab)
{
    private int? pendingIndex;
    public string? BeginHistory(int offset)
    {
        var index = tab.History.Index + offset;
        if (offset is not (-1 or 1) || index < 0 || index >= tab.History.Entries.Count) return null;
        pendingIndex = index;
        return tab.History.Entries[index];
    }
    public void Complete(string path)
    {
        var index = pendingIndex;
        pendingIndex = null;
        // リダイレクト等で別の場所に着いた場合は通常の移動として記録する。
        if (index is int i && !NavigationHistory.SameLocation(tab.History.Entries[i], path)) index = null;
        tab.NavigationSucceeded(path, index);
    }
    public void Fail(string message) { pendingIndex = null; tab.NavigationFailed(message); }
}

public sealed class PaneState : ObservableState
{
    public Guid Id { get; } = Guid.NewGuid();
    private readonly ObservableCollection<TabState> tabs = [];
    public ReadOnlyObservableCollection<TabState> Tabs { get; }
    private TabState selectedTab;
    public TabState SelectedTab => selectedTab;

    public PaneState(string initialPath)
    {
        selectedTab = new(initialPath);
        tabs.Add(selectedTab);
        Tabs = new(tabs);
    }
    public TabState AddTab(string path)
    {
        var tab = new TabState(path);
        tabs.Add(tab);
        return tab;
    }
    public void SelectTab(TabState tab)
    {
        if (!tabs.Contains(tab)) throw new ArgumentException("このペインのタブではありません。", nameof(tab));
        Set(ref selectedTab, tab, nameof(SelectedTab));
    }
    public bool CloseTab(TabState tab)
    {
        var index = tabs.IndexOf(tab);
        if (index < 0 || tabs.Count == 1) return false;
        if (tab == selectedTab) SelectTab(tabs[index == tabs.Count - 1 ? index - 1 : index + 1]);
        tabs.Remove(tab);
        return true;
    }
    public void MoveTab(TabState tab, int newIndex)
    {
        var oldIndex = tabs.IndexOf(tab);
        if (oldIndex < 0) throw new ArgumentException("このペインのタブではありません。", nameof(tab));
        if (newIndex < 0 || newIndex >= tabs.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
        if (oldIndex != newIndex) tabs.Move(oldIndex, newIndex);
    }
}

public sealed class WorkspaceState : ObservableState
{
    public SidebarState Sidebar { get; } = new();
    public PaneState Left { get; }
    public PaneState Right { get; }
    private PaneState activePane;
    public PaneState ActivePane => activePane;
    private double leftPaneRatio = 0.5;
    public double LeftPaneRatio
    {
        get => leftPaneRatio;
        set
        {
            if (!double.IsFinite(value) || value <= 0 || value >= 1) throw new ArgumentOutOfRangeException(nameof(value));
            Set(ref leftPaneRatio, value);
        }
    }
    public WorkspaceState(string left, string right) { Left = new(left); Right = new(right); activePane = Left; }
    public void Activate(PaneState pane)
    {
        if (pane != Left && pane != Right) throw new ArgumentException("このウィンドウのペインではありません。", nameof(pane));
        Set(ref activePane, pane, nameof(ActivePane));
    }
    public PaneState OtherPane(PaneState pane) => pane == Left ? Right : pane == Right ? Left : throw new ArgumentException("不明なペインです。");
}
