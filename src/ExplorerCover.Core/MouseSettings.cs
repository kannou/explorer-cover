using System.Text.Json;

namespace ExplorerCover.Core;

public enum TabCloseButton { None, Middle, Right, XButton1, XButton2 }

public sealed record MouseSettings(TabCloseButton CloseTabButton = TabCloseButton.Middle)
{
    public static MouseSettings FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("マウス設定にはオブジェクトが必要です。");
        var seen = new HashSet<string>();
        foreach (var property in root.EnumerateObject())
            if (!seen.Add(property.Name) || property.Name is not ("version" or "closeTabButton"))
                throw new FormatException($"不明または重複したマウス設定: {property.Name}");
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
            throw new FormatException("対応するマウス設定バージョンは1です。");
        if (!root.TryGetProperty("closeTabButton", out var button)) return new();
        if (button.ValueKind != JsonValueKind.String) throw new FormatException("closeTabButtonには文字列を指定してください。");
        return new(button.GetString() switch
        {
            "none" => TabCloseButton.None,
            "middle" => TabCloseButton.Middle,
            "right" => TabCloseButton.Right,
            "xButton1" => TabCloseButton.XButton1,
            "xButton2" => TabCloseButton.XButton2,
            _ => throw new FormatException("closeTabButtonはnone、middle、right、xButton1、xButton2のいずれかです。")
        });
    }
}
