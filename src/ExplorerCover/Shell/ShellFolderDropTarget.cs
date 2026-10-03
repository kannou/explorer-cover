using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ExplorerCover.Shell;

[StructLayout(LayoutKind.Sequential)]
internal struct DropPoint { public int X, Y; }

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellDropTarget
{
    void DragEnter(ComDataObject data, uint keys, DropPoint point, ref uint effect);
    void DragOver(uint keys, DropPoint point, ref uint effect);
    void DragLeave();
    [PreserveSig] int Drop(ComDataObject data, uint keys, DropPoint point, ref uint effect);
}

// データをファイルパスへ変換せず、Shellのコピー・移動判定と確認画面を利用する。
internal sealed class ShellFolderDropTarget(IShellDropTarget target) : IDisposable
{
    private bool entered, disposed;

    public static ShellFolderDropTarget Create(string path)
    {
        var iid = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out var item));
        nint pointer = 0;
        try
        {
            const uint folder = 0x20000000, fileSystem = 0x40000000;
            item.GetAttributes(folder | fileSystem, out var attributes);
            if ((attributes & (folder | fileSystem)) != (folder | fileSystem))
                throw new ArgumentException("ファイルシステムのフォルダーにドロップしてください。", nameof(path));
            var handler = new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5"); // BHID_SFUIObject
            iid = typeof(IShellDropTarget).GUID;
            item.BindToHandler(0, ref handler, ref iid, out pointer);
            return new((IShellDropTarget)Marshal.GetObjectForIUnknown(pointer));
        }
        finally
        {
            if (pointer != 0) Marshal.Release(pointer);
            Marshal.ReleaseComObject(item);
        }
    }

    public DragDropEffects Enter(ComDataObject data, DragDropKeyStates keys, Point screen, DragDropEffects allowed)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var effect = (uint)allowed;
        entered = true;
        target.DragEnter(data, (uint)keys, Point(screen), ref effect);
        return (DragDropEffects)effect & allowed;
    }
    public DragDropEffects Over(DragDropKeyStates keys, Point screen, DragDropEffects allowed)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var effect = (uint)allowed;
        target.DragOver((uint)keys, Point(screen), ref effect);
        return (DragDropEffects)effect & allowed;
    }
    public DragDropEffects Drop(ComDataObject data, DragDropKeyStates keys, Point screen, DragDropEffects allowed)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var effect = (uint)allowed;
        // Drop自体がDragLeave相当の後始末を行う。
        entered = false;
        DiagnosticLog.Write("Shell drop started");
        var hr = target.Drop(data, (uint)keys, Point(screen), ref effect);
        DiagnosticLog.Write($"Shell drop returned: HRESULT=0x{hr:X8}");
        Marshal.ThrowExceptionForHR(hr);
        if (hr == 0x40101) return DragDropEffects.None; // DRAGDROP_S_CANCEL
        return (DragDropEffects)effect & allowed;
    }
    private static DropPoint Point(Point point) => new() { X = (int)point.X, Y = (int)point.Y };
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (entered) target.DragLeave(); }
        catch (COMException ex) { DiagnosticLog.Write("Tab drop leave: " + ex.Message); }
        finally { if (Marshal.IsComObject(target)) Marshal.ReleaseComObject(target); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);
}
