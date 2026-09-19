using System.IO;

namespace ExplorerCover;

internal static class DiagnosticLog
{
    internal static void Write(string message)
    {
        var path = Environment.GetEnvironmentVariable("EXPLORER_COVER_LOG");
        if (string.IsNullOrEmpty(path)) return;
        try { File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
