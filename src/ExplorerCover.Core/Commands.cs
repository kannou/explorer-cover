namespace ExplorerCover.Core;

[Flags]
public enum InputScope { None = 0, Browser = 1, Address = 2, Chrome = 4 }

public static class CommandIds
{
    public const string Copy = "copy";
    public const string Cut = "cut";
    public const string Paste = "paste";
    public const string Delete = "delete";
    public const string Rename = "rename";
    public const string QuickView = "quickView";
    public const string FocusAddress = "focusAddress";
    public const string FocusFiles = "focusFiles";
    public const string SwitchPane = "switchPane";
    public const string NavigateAddress = "navigateAddress";
    public const string NewTab = "newTab";
    public const string DuplicateTab = "duplicateTab";
    public const string CloseTab = "closeTab";
    public const string NextTab = "nextTab";
    public const string PreviousTab = "previousTab";
    public const string Back = "back";
    public const string Forward = "forward";
    public const string Parent = "parent";
}

public sealed record CommandDefinition(string Id, string Name, InputScope Scopes, string DefaultGesture);

public static class CommandCatalog
{
    public static IReadOnlyList<CommandDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new CommandDefinition(CommandIds.Copy, "選択項目をコピー", InputScope.Browser, "Ctrl+C"),
        new CommandDefinition(CommandIds.Cut, "選択項目を切り取り", InputScope.Browser, "Ctrl+X"),
        new CommandDefinition(CommandIds.Paste, "現在のフォルダーに貼り付け", InputScope.Browser, "Ctrl+V"),
        new CommandDefinition(CommandIds.Delete, "選択項目を削除", InputScope.Browser, "Delete"),
        new CommandDefinition(CommandIds.Rename, "選択項目の名前を変更", InputScope.Browser, "F2"),
        new CommandDefinition(CommandIds.QuickView, "QuickLookでプレビュー", InputScope.Browser, "Space"),
        new CommandDefinition(CommandIds.FocusAddress, "パス入力へ移動", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Ctrl+L"),
        new CommandDefinition(CommandIds.FocusFiles, "パス編集を取り消して一覧へ戻る", InputScope.Address, "Escape"),
        new CommandDefinition(CommandIds.SwitchPane, "左右のペインを切り替え", InputScope.Browser | InputScope.Address | InputScope.Chrome, "F6"),
        new CommandDefinition(CommandIds.NavigateAddress, "入力したパスへ移動", InputScope.Address, "Enter"),
        new CommandDefinition(CommandIds.NewTab, "新しいタブ", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Ctrl+T"),
        new CommandDefinition(CommandIds.DuplicateTab, "タブを複製", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Ctrl+Shift+T"),
        new CommandDefinition(CommandIds.CloseTab, "タブを閉じる", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Ctrl+W"),
        new CommandDefinition(CommandIds.NextTab, "次のタブ", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Ctrl+Tab"),
        new CommandDefinition(CommandIds.PreviousTab, "前のタブ", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Ctrl+Shift+Tab"),
        new CommandDefinition(CommandIds.Back, "戻る", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Alt+Left"),
        new CommandDefinition(CommandIds.Forward, "進む", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Alt+Right"),
        new CommandDefinition(CommandIds.Parent, "ひとつ上へ", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Alt+Up")
    });
}

// 実行先をキー押下時／クリック時に指定する。登録時のアクティブペインを捕まえない。
public sealed class CommandDispatcher
{
    private readonly Dictionary<string, (Action<PaneState> Run, Predicate<PaneState> CanRun)> commands = [];
    public void Register(string id, Action<PaneState> execute, Predicate<PaneState>? canExecute = null)
    {
        if (!CommandCatalog.All.Any(c => c.Id == id)) throw new ArgumentException("未定義のコマンドです。", nameof(id));
        commands.Add(id, (execute, canExecute ?? (_ => true)));
    }
    public bool CanExecute(string id, PaneState pane) => commands.TryGetValue(id, out var command) && command.CanRun(pane);
    public bool Execute(string id, PaneState pane)
    {
        if (!commands.TryGetValue(id, out var command) || !command.CanRun(pane)) return false;
        command.Run(pane);
        return true;
    }
}
