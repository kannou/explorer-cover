using System.Runtime.InteropServices;

namespace ExplorerCover.Shell;

// コピー元のパスを確定してから受理する。移動や遅延生成される仮想ファイルには使わない。
internal static class ShellCopyOperation
{
    private static readonly HashSet<Task<bool>> pending = [];

    internal static bool IsWslPath(string path) =>
        path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase);

    public static Task<bool> Start(string[] paths, string destination)
    {
        var sources = (string[])paths.Clone();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (pending) pending.Add(completion.Task);
        var thread = new Thread(() =>
        {
            bool result = false;
            Exception? error = null;
            var initialized = false;
            try
            {
                Marshal.ThrowExceptionForHR(OleInitialize(0));
                initialized = true;
                result = Copy(sources, destination);
            }
            catch (Exception ex) { error = ex; }
            finally { if (initialized) OleUninitialize(); }
            if (error != null) completion.SetException(error); else completion.SetResult(result);
            lock (pending) pending.Remove(completion.Task);
        }) { IsBackground = true, Name = "Shell file copy" };
        try { thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
        catch { lock (pending) pending.Remove(completion.Task); throw; }
        return completion.Task;
    }

    public static async Task WaitForIdleAsync()
    {
        Task<bool>[] operations;
        lock (pending) operations = pending.ToArray();
        try { await Task.WhenAll(operations); }
        catch (Exception ex) { DiagnosticLog.Write("Shell copy ended with an error: " + ex.Message); }
    }

    private static bool Copy(string[] paths, string destination)
    {
        // UIで作成したCOMオブジェクトは渡さない。参照先もこのSTA内で作成・解放する。
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
        IShellItem? folder = null;
        try
        {
            folder = Item(destination);
            foreach (var path in paths)
            {
                var source = Item(path);
                try { operation.CopyItem(source, folder, null, 0); }
                finally { Marshal.ReleaseComObject(source); }
            }
            // 標準の進捗・同名確認・取消を利用する。メインウィンドウを所有者にしない。
            DiagnosticLog.Write($"Shell background copy started: {destination}; items={paths.Length}");
            var hr = operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            DiagnosticLog.Write($"Shell background copy ended: HRESULT=0x{hr:X8}; aborted={aborted}");
            if (aborted) return false;
            Marshal.ThrowExceptionForHR(hr);
            return true;
        }
        finally
        {
            if (folder != null) Marshal.ReleaseComObject(folder);
            Marshal.ReleaseComObject(operation);
        }
    }

    private static IShellItem Item(string path)
    {
        var iid = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out var item));
        return item;
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);
}

// メソッドの順序はWindows SDKのIFileOperationと一致させる。
[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    void Advise(nint sink, out uint cookie);
    void Unadvise(uint cookie);
    void SetOperationFlags(uint flags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    void SetProgressDialog(nint dialog);
    void SetProperties(nint properties);
    void SetOwnerWindow(nint hwnd);
    void ApplyPropertiesToItem(IShellItem item);
    void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
    void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, nint sink);
    void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
    void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, nint sink);
    void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
    void DeleteItem(IShellItem item, nint sink);
    void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, nint sink);
    [PreserveSig] int PerformOperations();
    void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}
