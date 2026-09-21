using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace ExplorerCover.Shell;

// Shellの確認画面はアプリの削除コマンドを経由せず表示される場合もある。
// メニュー終了ではなく、実際のダイアログ破棄を検知して復帰させる。
internal sealed class ShellDialogFocus : IDisposable
{
    private readonly WinEventCallback callback;
    private readonly Dictionary<nint, Action> dialogs = [];
    private nint hook;

    internal ShellDialogFocus(nint owner, Dispatcher dispatcher, Func<Action?> captureRestore)
    {
        callback = (_, eventId, hwnd, objectId, childId, _, _) =>
        {
            if (hook == 0 || hwnd == 0 || objectId != 0 || childId != 0) return;
            if (eventId == 0x8002) // EVENT_OBJECT_SHOW
            {
                var name = new StringBuilder(64);
                Native.GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() != "#32770" || GetAncestor(hwnd, 3) != owner) return; // GA_ROOTOWNER
                var restore = captureRestore();
                if (restore == null) return;
                dialogs[hwnd] = restore;
                DiagnosticLog.Write("Shell dialog opened");
            }
            else if (eventId == 0x8001 && dialogs.Remove(hwnd, out var restore)) // EVENT_OBJECT_DESTROY
            {
                dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    if (hook == 0 || GetForegroundWindow() != owner) return;
                    restore();
                });
                DiagnosticLog.Write("Shell dialog closed");
            }
        };
        hook = SetWinEventHook(0x8001, 0x8002, 0, callback, (uint)Environment.ProcessId, 0, 0);
        if (hook == 0) DiagnosticLog.Write($"Shell dialog hook unavailable: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
    }

    public void Dispose()
    {
        if (hook != 0) { UnhookWinEvent(hook); hook = 0; }
        dialogs.Clear();
    }

    private delegate void WinEventCallback(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
