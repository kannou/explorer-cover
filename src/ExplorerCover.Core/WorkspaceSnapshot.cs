using System.Text.Json;

namespace ExplorerCover.Core;

public sealed record PaneSnapshot(string[] Paths, int SelectedIndex);
public sealed record BookmarkSnapshot(string Name, string Path);
public sealed record WorkspaceSnapshot(int Version, PaneSnapshot Left, PaneSnapshot Right, string ActivePane,
    double LeftPaneRatio, double SidebarWidth, BookmarkSnapshot[] Bookmarks, WindowSnapshot? Window = null)
{
    private static readonly JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static WorkspaceSnapshot Capture(WorkspaceState state) => new(1,
        CapturePane(state.Left), CapturePane(state.Right), state.ActivePane == state.Left ? "left" : "right",
        state.LeftPaneRatio, state.Sidebar.Width, state.Sidebar.Bookmarks.Select(b => new BookmarkSnapshot(b.Name, b.Path)).ToArray());
    private static PaneSnapshot CapturePane(PaneState pane) => new(pane.Tabs.Select(t => t.CurrentPath ?? t.InitialPath).ToArray(), pane.Tabs.IndexOf(pane.SelectedTab));
    public string ToJson() { Validate(); return JsonSerializer.Serialize(this, options); }
    public static WorkspaceSnapshot FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("状態ファイルはオブジェクトである必要があります。");
        if (!document.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number)) throw new FormatException("状態ファイルのバージョンがありません。");
        if (number != 1) throw new NotSupportedException("このバージョンの状態ファイルには対応していません。");
        var snapshot = JsonSerializer.Deserialize<WorkspaceSnapshot>(json, options) ?? throw new FormatException("状態ファイルが空です。");
        snapshot.Validate(); return snapshot;
    }
    private void Validate()
    {
        static bool ValidText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 32767 && !text.Any(char.IsControl);
        static void Pane(PaneSnapshot? pane)
        {
            if (pane?.Paths == null || pane.Paths.Length is < 1 or > 1024 || pane.SelectedIndex < 0 || pane.SelectedIndex >= pane.Paths.Length || pane.Paths.Any(p => !ValidText(p))) throw new FormatException("タブの保存形式が不正です。");
        }
        if (Version != 1) throw new NotSupportedException("状態ファイルのバージョンが不正です。");
        Pane(Left); Pane(Right);
        if (Window != null && !Window.IsValid) throw new FormatException("ウィンドウ位置の保存形式が不正です。");
        if (ActivePane is not ("left" or "right") || !double.IsFinite(LeftPaneRatio) || LeftPaneRatio <= 0 || LeftPaneRatio >= 1 || !double.IsFinite(SidebarWidth) || SidebarWidth is < 160 or > 380) throw new FormatException("レイアウトの保存形式が不正です。");
        if (Bookmarks == null || Bookmarks.Length > 4096 || Bookmarks.Any(b => b == null || !ValidText(b.Name) || !ValidText(b.Path))) throw new FormatException("ブックマークの保存形式が不正です。");
    }
    public WorkspaceState Restore()
    {
        Validate();
        var state = new WorkspaceState(Left.Paths[0], Right.Paths[0]) { LeftPaneRatio = LeftPaneRatio };
        static void RestorePane(PaneState pane, PaneSnapshot snapshot)
        {
            foreach (var path in snapshot.Paths.Skip(1)) pane.AddTab(path);
            pane.SelectTab(pane.Tabs[snapshot.SelectedIndex]);
        }
        RestorePane(state.Left, Left); RestorePane(state.Right, Right);
        state.Activate(ActivePane == "left" ? state.Left : state.Right);
        state.Sidebar.Width = SidebarWidth;
        foreach (var bookmark in Bookmarks) state.Sidebar.Add(bookmark.Name, bookmark.Path);
        return state;
    }
}
