using System.IO;
using System.Text.Json;
using ExplorerCover.Core;

namespace ExplorerCover.Commands;

internal static class InputSettingsFile
{
    public static string PathName => Environment.GetEnvironmentVariable("EXPLORER_COVER_SETTINGS")
        ?? (Environment.GetEnvironmentVariable("EXPLORER_COVER_SHORTCUTS") is string keys ? keys + ".input.json" :
            Environment.GetEnvironmentVariable("EXPLORER_COVER_MOUSE") is string mouse ? mouse + ".input.json" :
            Environment.GetEnvironmentVariable("EXPLORER_COVER_QUICKLOOK") is string preview ? preview + ".input.json" :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "explorer_cover", "input.json"));
    public static (InputSettings? Settings, string? Warning) Load()
    {
        try { return (File.Exists(PathName) ? InputSettings.FromJson(File.ReadAllText(PathName)) : null, null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException)
        { return (null, "入力設定を読み込めないため、従来の設定を使用します: " + ex.Message); }
    }
    public static void Save(InputSettings settings)
    {
        var json = settings.ToJson(); InputSettings.FromJson(json);
        AtomicFile.Write(PathName, json);
    }
}
