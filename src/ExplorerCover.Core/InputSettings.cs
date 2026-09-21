using System.Text.Json;

namespace ExplorerCover.Core;

public sealed record InputSettings(ShortcutMap Shortcuts, MouseSettings Mouse, QuickLookSettings? QuickLook = null)
{
    public static InputSettings FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1) throw new FormatException("対応する入力設定バージョンは1です。");
        var names = new HashSet<string>();
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name) || property.Name is not ("version" or "shortcuts" or "mouse" or "quickLook")) throw new FormatException("不明または重複した入力設定です。");
        if (!root.TryGetProperty("shortcuts", out var shortcuts) || !root.TryGetProperty("mouse", out var mouse)) throw new FormatException("キーとマウスの設定が必要です。");
        return new(ShortcutMap.FromJson(shortcuts.GetRawText()), MouseSettings.FromJson(mouse.GetRawText()),
            root.TryGetProperty("quickLook", out var quickLook) && quickLook.ValueKind != JsonValueKind.Null ? QuickLookSettings.FromJson(quickLook.GetRawText()) : null);
    }
    public static string ShortcutJson(ShortcutMap map) => JsonSerializer.Serialize(new { version = 1, bindings = CommandCatalog.All.ToDictionary(c => c.Id, c => map.Bindings.Where(b => b.CommandId == c.Id).Select(b => b.Gesture.ToString()).ToArray()) });
    public string ToJson() => JsonSerializer.Serialize(new
    {
        version = 1,
        quickLook = QuickLook == null ? null : new { version = 1, executablePath = QuickLook.ExecutablePath },
        shortcuts = JsonSerializer.Deserialize<JsonElement>(ShortcutJson(Shortcuts)),
        mouse = new { version = 1, closeTabButton = MouseSettings.ButtonName(Mouse.CloseTabButton), openBookmarkInNewTabButton = MouseSettings.ButtonName(Mouse.OpenBookmarkInNewTabButton) }
    }, new JsonSerializerOptions { WriteIndented = true });
}
