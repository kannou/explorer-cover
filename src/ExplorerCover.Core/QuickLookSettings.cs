using System.Text.Json;

namespace ExplorerCover.Core;

public sealed record QuickLookSettings(string? ExecutablePath = null)
{
    public static QuickLookSettings FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("QuickLook設定はオブジェクトで指定してください。");
        var seen = new HashSet<string>();
        foreach (var p in root.EnumerateObject())
            if (!seen.Add(p.Name) || p.Name is not ("version" or "executablePath")) throw new FormatException($"不明または重複した設定: {p.Name}");
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
            throw new FormatException("対応する設定バージョンは1です。");
        string? path = null;
        if (root.TryGetProperty("executablePath", out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.String) throw new FormatException("executablePathは文字列またはnullで指定してください。");
            path = Environment.ExpandEnvironmentVariables(value.GetString()!);
            if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.IndexOfAny(['\r', '\n', '"']) >= 0)
                throw new FormatException("executablePathにはQuickLook.exeの絶対パスを指定してください。");
        }
        return new(path);
    }
}
