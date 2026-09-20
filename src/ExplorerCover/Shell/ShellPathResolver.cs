using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerCover.Shell;

// COMオブジェクトは渡さず、絶対PIDLのバイト列だけをUIのSTAへ返す。
internal static class ShellPathResolver
{
    private static readonly BlockingCollection<Action> queue = new(32);
    static ShellPathResolver()
    {
        for (var i = 0; i < 2; i++)
        {
            var thread = new Thread(() =>
            {
                var hr = CoInitializeEx(0, 2);
                try { foreach (var work in queue.GetConsumingEnumerable()) work(); }
                finally { if (hr >= 0) CoUninitialize(); }
            }) { IsBackground = true, Name = "Shell path resolver" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
    }
    public static Task<byte[]> ResolveAsync(string path, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryAdd(() =>
        {
            nint pidl = 0;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) == 0)
                    throw new IOException((attributes & FileAttributes.ReparsePoint) != 0 ? "Windowsからフォルダーとして開けないリンクです。リンク先の実パスを指定してください。" : "フォルダーのパスを指定してください。");
                // UNCのアクセス拒否をBrowseToIDListのモーダルエラーより前に検出する。
                // 全件を読む必要はなく、列挙を開始できるかだけを作業スレッドで確認する。
                if (path.StartsWith(@"\\"))
                {
                    using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                    entries.MoveNext();
                }
                cancellation.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(Native.SHParseDisplayName(path, 0, out pidl, 0, out _));
                cancellation.ThrowIfCancellationRequested();
                var bytes = new byte[checked((int)ILGetSize(pidl))];
                Marshal.Copy(pidl, bytes, 0, bytes.Length);
                completion.TrySetResult(bytes);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellation); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
        })) completion.TrySetException(new IOException("移動先の確認が混み合っています。しばらくして再試行してください。"));
        return completion.Task;
    }
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
