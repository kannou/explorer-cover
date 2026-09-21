using System.Text.Json;

namespace ExplorerCover.Core;

public enum TabCloseButton { None, Middle, Right, XButton1, XButton2 }

public sealed record MouseSettings(TabCloseButton CloseTabButton = TabCloseButton.Middle,
    TabCloseButton OpenBookmarkInNewTabButton = TabCloseButton.Middle)
{
    public static MouseSettings FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("マウス設定にはオブジェクトが必要です。");
        var seen = new HashSet<string>();
        foreach (var property in root.EnumerateObject())
            if (!seen.Add(property.Name) || property.Name is not ("version" or "closeTabButton" or "openBookmarkInNewTabButton"))
                throw new FormatException($"不明または重複したマウス設定: {property.Name}");
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
            throw new FormatException("対応するマウス設定バージョンは1です。");
        return new(ReadButton(root, "closeTabButton"), ReadButton(root, "openBookmarkInNewTabButton"));
    }

    private static TabCloseButton ReadButton(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var button)) return TabCloseButton.Middle;
        if (button.ValueKind != JsonValueKind.String) throw new FormatException($"{name}には文字列を指定してください。");
        return button.GetString() switch
        {
            "none" => TabCloseButton.None,
            "middle" => TabCloseButton.Middle,
            "right" => TabCloseButton.Right,
            "xButton1" => TabCloseButton.XButton1,
            "xButton2" => TabCloseButton.XButton2,
            _ => throw new FormatException($"{name}はnone、middle、right、xButton1、xButton2のいずれかです。")
        };
    }

    public static string ButtonName(TabCloseButton button) => button switch
    {
        TabCloseButton.None => "none", TabCloseButton.Middle => "middle", TabCloseButton.Right => "right",
        TabCloseButton.XButton1 => "xButton1", TabCloseButton.XButton2 => "xButton2",
        _ => throw new FormatException("未対応のマウスボタンです。")
    };
}
