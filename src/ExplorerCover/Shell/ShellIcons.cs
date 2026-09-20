using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerCover.Shell;

// Shell拡張のアイコン取得はSTAの作業スレッドで実行し、UIへは凍結した画像だけを返す。
internal sealed class ShellIcons : IDisposable
{
    private readonly BlockingCollection<Action> queue = new();
    private readonly Dictionary<string, Task<ImageSource?>> cache = new(StringComparer.Ordinal);
    private volatile bool disposed;
    public ShellIcons()
    {
        for (var i = 0; i < 2; i++)
        {
            var thread = new Thread(() =>
            {
                var hr = CoInitializeEx(0, 2);
                try { foreach (var action in queue.GetConsumingEnumerable()) action(); }
                finally { if (hr >= 0) CoUninitialize(); }
            }) { IsBackground = true, Name = "Shell icons" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
    }
    public Task<ImageSource?> GetAsync(string? path)
    {
        if (disposed) return Task.FromResult<ImageSource?>(null);
        var key = path ?? "<folder>";
        if (cache.TryGetValue(key, out var cached)) return cached;
        if (cache.Count >= 256)
        {
            var removable = cache.FirstOrDefault(p => p.Key != "<folder>" && p.Value.IsCompleted);
            if (removable.Key != null) cache.Remove(removable.Key);
            else return Task.FromResult<ImageSource?>(null);
        }
        var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.Add(key, completion.Task);
        queue.Add(() =>
        {
            try { completion.SetResult(disposed ? null : Read(path)); }
            catch (Exception ex) { DiagnosticLog.Write("Shell icon unavailable: " + ex.Message); completion.SetResult(null); }
        });
        return completion.Task;
    }
    private static ImageSource? Read(string? path)
    {
        var info = new FileInfo();
        try
        {
            if (SHGetFileInfo(path ?? "folder", 0x10, ref info, (uint)Marshal.SizeOf<FileInfo>(), 0x101u | (path == null ? 0x10u : 0)) == 0 || info.Icon == 0) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); return image;
        }
        finally { if (info.Icon != 0) DestroyIcon(info.Icon); }
    }
    public void Dispose() { disposed = true; cache.Clear(); queue.CompleteAdding(); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public nint Icon; public int Index; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, ref FileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
