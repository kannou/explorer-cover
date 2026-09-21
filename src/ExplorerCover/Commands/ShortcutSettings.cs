using System.IO;
using System.Text.Json;
using ExplorerCover.Core;

namespace ExplorerCover.Commands;

internal static class ShortcutSettings
{
    public static (ShortcutService Service, string? Warning) Load()
    {
        var service = new ShortcutService();
        var customPath = Environment.GetEnvironmentVariable("EXPLORER_COVER_SHORTCUTS");
        var path = customPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "explorer-cover", "shortcuts.json");
        try
        {
            if (customPath != null || File.Exists(path)) service.ApplyJson(File.ReadAllText(path));
            return (service, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException or NotSupportedException)
        {
            var warning = $"キー設定を読み込めないため初期値を使用します: {ex.Message}";
            DiagnosticLog.Write(warning);
            return (service, warning);
        }
    }
}
