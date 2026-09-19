namespace ExplorerCover.Core;

[Flags]
public enum InputScope { None = 0, Browser = 1, Address = 2, Chrome = 4 }

public static class CommandIds
{
    public const string FocusAddress = "focusAddress";
    public const string FocusFiles = "focusFiles";
    public const string SwitchPane = "switchPane";
    public const string NavigateAddress = "navigateAddress";
}

public sealed record CommandDefinition(string Id, string Name, InputScope Scopes, string DefaultGesture);

public static class CommandCatalog
{
    public static IReadOnlyList<CommandDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new CommandDefinition(CommandIds.FocusAddress, "パス入力へ移動", InputScope.Browser | InputScope.Address | InputScope.Chrome, "Ctrl+L"),
        new CommandDefinition(CommandIds.FocusFiles, "パス編集を取り消して一覧へ戻る", InputScope.Address, "Escape"),
        new CommandDefinition(CommandIds.SwitchPane, "左右のペインを切り替え", InputScope.Browser | InputScope.Address | InputScope.Chrome, "F6"),
        new CommandDefinition(CommandIds.NavigateAddress, "入力したパスへ移動", InputScope.Address, "Enter")
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
