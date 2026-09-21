using System.Diagnostics;
using System.Reflection;

namespace ExplorerCover;

internal static class ProductInfo
{
    public const string Name = "explorer-cover";
    public static string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "不明";
    public static string FileVersion
    {
        get
        {
            var location = Assembly.GetEntryAssembly()?.Location;
            return string.IsNullOrEmpty(location) ? "不明" : FileVersionInfo.GetVersionInfo(location).FileVersion ?? "不明";
        }
    }
}
