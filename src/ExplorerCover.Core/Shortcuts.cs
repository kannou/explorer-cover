using System.Text.Json;

namespace ExplorerCover.Core;

[Flags]
public enum KeyModifiers { None = 0, Control = 1, Shift = 2, Alt = 4 }

public readonly record struct ShortcutGesture(int VirtualKey, KeyModifiers Modifiers)
{
    private static readonly Dictionary<string, int> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Escape"] = 0x1B, ["Tab"] = 0x09, ["Space"] = 0x20,
        ["Back"] = 0x08, ["Delete"] = 0x2E, ["Insert"] = 0x2D, ["Home"] = 0x24,
        ["End"] = 0x23, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22
    };
    public static ShortcutGesture Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("キーが空です。");
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" => KeyModifiers.Control, "SHIFT" => KeyModifiers.Shift, "ALT" => KeyModifiers.Alt,
                _ => throw new FormatException($"不明な修飾キー: {part}")
            };
            if ((modifiers & modifier) != 0) throw new FormatException("修飾キーが重複しています。");
            modifiers |= modifier;
        }
        var key = parts[^1].ToUpperInvariant();
        int vk;
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) vk = key[0];
        else if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24) vk = 0x6F + f;
        else if (!Names.TryGetValue(key, out vk)) throw new FormatException($"未対応のキー: {key}");
        return new(vk, modifiers);
    }
    public override string ToString()
    {
        var virtualKey = VirtualKey;
        var key = VirtualKey is >= 0x70 and <= 0x87 ? $"F{VirtualKey - 0x6F}" :
            Names.FirstOrDefault(p => p.Value == virtualKey).Key ?? ((char)VirtualKey).ToString();
        return (Modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") +
            (Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "") +
            (Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "") + key;
    }
}

public sealed record ShortcutBinding(string CommandId, ShortcutGesture Gesture, InputScope Scopes);

public sealed class ShortcutMap
{
    public IReadOnlyList<ShortcutBinding> Bindings { get; }
    public ShortcutMap(IReadOnlyDictionary<string, string[]>? overrides = null)
    {
        overrides ??= new Dictionary<string, string[]>();
        foreach (var id in overrides.Keys)
            if (!CommandCatalog.All.Any(c => c.Id == id)) throw new FormatException($"不明なコマンド: {id}");
        var bindings = new List<ShortcutBinding>();
        foreach (var command in CommandCatalog.All)
        {
            var gestures = overrides.TryGetValue(command.Id, out var configured) ? configured : [command.DefaultGesture];
            if (gestures == null) throw new FormatException($"{command.Id}: キーの配列が必要です。");
            foreach (var text in gestures)
            {
                var gesture = ShortcutGesture.Parse(text);
                ValidateGesture(gesture, command);
                if (bindings.Any(b => b.Gesture == gesture && (b.Scopes & command.Scopes) != 0))
                    throw new FormatException($"キーが競合しています: {gesture}");
                bindings.Add(new(command.Id, gesture, command.Scopes));
            }
        }
        Bindings = bindings.AsReadOnly();
    }

    private static void ValidateGesture(ShortcutGesture gesture, CommandDefinition command)
    {
        var scopes = command.Scopes;
        // 編集キーとOS／シェルの既存操作は、この段階では変更対象にしない。
        var key = gesture.VirtualKey;
        var mods = gesture.Modifiers;
        if (key is 0x0D or 0x1B && scopes != InputScope.Address)
            throw new FormatException("EnterとEscapeはパス欄の操作にのみ割り当てられます。");
        if (key is 0x0D or 0x1B && mods != KeyModifiers.None)
            throw new FormatException("EnterとEscapeの修飾キー付き割り当ては未対応です。");
        if (key == 0x20)
        {
            if (command.Id == CommandIds.QuickView && mods == KeyModifiers.None) return;
            throw new FormatException("Spaceは修飾キーなしでQuickLookにのみ割り当てられます。");
        }
        var tabSwitch = key == 0x09 && mods is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift);
        var navigation = key is 0x25 or 0x26 or 0x27 && mods == KeyModifiers.Alt;
        if (!tabSwitch && !navigation && (key == 0x09 || key is 0x08 or 0x2D or 0x2E || key is >= 0x21 and <= 0x28))
            throw new FormatException("Tab・編集・カーソル移動キーは予約されています。");
        if (mods == KeyModifiers.Alt && key == 0x73 || mods == KeyModifiers.Control && key == 0x1B)
            throw new FormatException("OSの操作に予約されたキーです。");
        if (mods.HasFlag(KeyModifiers.Control) && !mods.HasFlag(KeyModifiers.Alt) && key is 0x41 or 0x43 or 0x56 or 0x58 or 0x59 or 0x5A)
            throw new FormatException("文字編集・シェル操作のキーは予約されています。");
        if (mods.HasFlag(KeyModifiers.Control) && mods.HasFlag(KeyModifiers.Alt))
            throw new FormatException("Ctrl+Altは文字入力との競合を避けるため使用できません。");
        if (key is >= 0x70 and <= 0x87)
        {
            if ((mods == KeyModifiers.None && key is 0x71 or 0x74) || key == 0x79) throw new FormatException("F2・F5・F10はシェル操作に予約されています。");
            return;
        }
        if (key is 0x0D or 0x1B && mods == KeyModifiers.None) return;
        if (!mods.HasFlag(KeyModifiers.Control) && !mods.HasFlag(KeyModifiers.Alt))
            throw new FormatException("文字キーにはCtrlまたはAltを付けてください。");
    }

    public string? Resolve(ShortcutGesture gesture, InputScope scope) =>
        scope == InputScope.None ? null : Bindings.FirstOrDefault(b => b.Gesture == gesture && (b.Scopes & scope) != 0)?.CommandId;

    public string Display(string commandId)
    {
        var display = string.Join(" / ", Bindings.Where(b => b.CommandId == commandId).Select(b => b.Gesture.ToString()));
        return display.Length == 0 ? "未割当" : display;
    }

    public static ShortcutMap FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("設定のルートはオブジェクトである必要があります。");
        var seen = new HashSet<string>();
        foreach (var property in root.EnumerateObject())
            if (!seen.Add(property.Name) || property.Name is not ("version" or "bindings")) throw new FormatException($"不明または重複した設定: {property.Name}");
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
            throw new FormatException("対応する設定バージョンは1です。");
        if (!root.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Object)
            throw new FormatException("bindingsオブジェクトが必要です。");
        var overrides = new Dictionary<string, string[]>();
        foreach (var property in bindings.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array) throw new FormatException("キー割り当ては配列で指定してください。");
            var keys = property.Value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new FormatException("キーは文字列で指定してください。")).ToArray();
            if (!overrides.TryAdd(property.Name, keys)) throw new FormatException($"コマンドが重複しています: {property.Name}");
        }
        return new(overrides);
    }
}

public sealed class ShortcutService
{
    public ShortcutMap Map { get; private set; } = new();
    public event Action? Changed;
    public void ApplyJson(string json)
    {
        var next = ShortcutMap.FromJson(json);
        Map = next;
        Changed?.Invoke();
    }
    public void Reset() { Map = new(); Changed?.Invoke(); }
}
