using System.IO;
using System.Text.Json;
using ExplorerCover.Core;

namespace ExplorerCover.Commands;

internal static class QuickLookSettingsFile
{
    public static (QuickLookSettings Settings, string? Warning) Load()
    {
        var custom = Environment.GetEnvironmentVariable("EXPLORER_COVER_QUICKLOOK");
        var path = custom ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "explorer_cover", "quicklook.json");
        try { return (custom != null || File.Exists(path) ? QuickLookSettings.FromJson(File.ReadAllText(path)) : new(), null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException or NotSupportedException)
        {
            var warning = $"QuickLook設定を読み込めないため自動検出を使用します: {ex.Message}";
            DiagnosticLog.Write(warning);
            return (new(), warning);
        }
    }
}
