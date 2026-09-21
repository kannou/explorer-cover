using System.IO;
using System.Text.Json;
using ExplorerCover.Core;

namespace ExplorerCover.Commands;

internal static class MouseSettingsFile
{
    public static (MouseSettings Settings, string? Warning) Load()
    {
        var customPath = Environment.GetEnvironmentVariable("EXPLORER_COVER_MOUSE");
        var path = customPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "explorer-cover", "mouse.json");
        try
        {
            return (customPath != null || File.Exists(path) ? MouseSettings.FromJson(File.ReadAllText(path)) : new(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException or NotSupportedException)
        {
            var warning = $"マウス設定を読み込めないため初期値を使用します: {ex.Message}";
            DiagnosticLog.Write(warning);
            return (new(), warning);
        }
    }
}
