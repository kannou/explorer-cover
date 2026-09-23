using System.Collections.ObjectModel;

namespace ExplorerCover.Core;

public sealed class BookmarkState(string name, string path) : ObservableState
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Path { get; } = path;
    private string name = name;
    public string Name => name;
    public string DisplayText => $"{Name} ({Path})";
    public void Rename(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Set(ref name, value.Trim(), nameof(Name));
        Notify(nameof(DisplayText));
    }
}

public sealed class SidebarState : ObservableState
{
    private readonly ObservableCollection<BookmarkState> bookmarks = [];
    private readonly Dictionary<LocationKey, BookmarkState> byLocation = [];
    public ReadOnlyObservableCollection<BookmarkState> Bookmarks { get; }
    private double width = 280;
    public double Width
    {
        get => width;
        set
        {
            if (!double.IsFinite(value) || value < 160 || value > 380) throw new ArgumentOutOfRangeException(nameof(value));
            Set(ref width, value);
        }
    }
    public SidebarState() => Bookmarks = new(bookmarks);
    public BookmarkState Add(string name, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var key = LocationKey.Create(path);
        if (byLocation.TryGetValue(key, out var existing)) return existing;
        var bookmark = new BookmarkState(name.Trim(), path);
        // コレクションの通知先から検索されても一致するよう、通知前に索引を更新する。
        byLocation.Add(key, bookmark);
        bookmarks.Add(bookmark);
        return bookmark;
    }
    public bool Remove(BookmarkState bookmark)
    {
        if (bookmark == null) return false;
        var key = LocationKey.Create(bookmark.Path);
        if (!byLocation.TryGetValue(key, out var existing) || !ReferenceEquals(existing, bookmark)) return false;
        byLocation.Remove(key);
        return bookmarks.Remove(bookmark);
    }
}

public enum DriveAvailability { Ready, Unavailable, Unknown }

public sealed record DriveSnapshot(string Path, string Name, long? TotalBytes, long? FreeBytes, string? Error = null, DriveAvailability Availability = DriveAvailability.Ready)
{
    public double UsedPercent => TotalBytes is > 0 && FreeBytes is long free ? Math.Clamp(100.0 * (1.0 - (double)free / TotalBytes.Value), 0, 100) : 0;
}

// UIスレッドから呼ぶ。遅いドライブを待つ間も他ドライブの結果を独立して反映する。
public sealed class DriveMonitor(Func<Task<string[]>> enumerate, Func<string, Task<DriveSnapshot>> query) : IDisposable
{
    private sealed class Entry { public bool Busy; public int Generation; }
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool enumerating;
    private bool disposed;
    public event Action<IReadOnlyCollection<string>>? PathsChanged;
    public event Action<DriveSnapshot>? Updated;
    public event Action<string>? Failed;
    public async Task RefreshAsync()
    {
        if (disposed || enumerating) return;
        enumerating = true;
        try
        {
            var paths = (await enumerate()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (disposed) return;
            foreach (var removed in currentPaths.Except(paths, StringComparer.OrdinalIgnoreCase))
                if (entries.TryGetValue(removed, out var old)) old.Generation++;
            foreach (var removed in entries.Keys.Except(paths, StringComparer.OrdinalIgnoreCase).ToArray())
                if (!entries[removed].Busy) entries.Remove(removed);
            currentPaths = new(paths, StringComparer.OrdinalIgnoreCase);
            PathsChanged?.Invoke(paths);
            foreach (var path in paths)
            {
                if (!entries.TryGetValue(path, out var entry)) entries.Add(path, entry = new());
                if (!entry.Busy) _ = QueryAsync(path, entry);
            }
        }
        catch (Exception ex) { if (!disposed) Failed?.Invoke(ex.Message); }
        finally { enumerating = false; }
    }
    private HashSet<string> currentPaths = new(StringComparer.OrdinalIgnoreCase);
    private async Task QueryAsync(string path, Entry entry)
    {
        entry.Busy = true;
        var generation = entry.Generation;
        try
        {
            var task = query(path);
            if (await Task.WhenAny(task, Task.Delay(3000)) != task && !disposed && currentPaths.Contains(path) && generation == entry.Generation)
                Updated?.Invoke(new(path, path, null, null, "応答を待っています…", DriveAvailability.Unknown));
            var result = await task;
            if (!disposed && currentPaths.Contains(path) && generation == entry.Generation) Updated?.Invoke(result);
        }
        catch (Exception ex) { if (!disposed && currentPaths.Contains(path) && generation == entry.Generation) Updated?.Invoke(new(path, path, null, null, ex.Message, DriveAvailability.Unknown)); }
        finally { entry.Busy = false; }
    }
    public void Dispose() => disposed = true;
}
