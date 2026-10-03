using System.Runtime.InteropServices;

namespace ExplorerCover.Shell;

// Shellが非同期処理を起動した後も、呼び出し元のSTAとメッセージ処理を保つ。
internal sealed class ShellThreadLifetime : IDisposable
{
    private nint count = Marshal.AllocCoTaskMem(sizeof(int));
    private nint reference;
    private readonly int thread = Environment.CurrentManagedThreadId;

    public ShellThreadLifetime()
    {
        Marshal.WriteInt32(count, 0);
        try
        {
            Marshal.ThrowExceptionForHR(SHCreateThreadRef(count, out reference));
            Marshal.ThrowExceptionForHR(SHSetThreadRef(reference));
        }
        catch { Dispose(); throw; }
    }

    internal int ReferenceCount => count == 0 ? 0 : Marshal.ReadInt32(count);

    public async Task WaitForIdleAsync()
    {
        // ShellのクリップボードデータもSTAを参照する。内容を残してCOM参照を解放する。
        // 他のアプリが所有するクリップボードには触れない。
        while (OwnsClipboard())
        {
            var hr = OleFlushClipboard();
            if (hr >= 0) break;
            await Task.Delay(50); // 他のプロセスがクリップボードを開いている間は再試行する。
        }
        if (ReferenceCount <= 1) return;
        DiagnosticLog.Write($"Waiting for Shell operations before shutdown: references={ReferenceCount}");
        // 同期WaitやJoinは使わない。ShellのCOM呼び戻しと進捗画面を処理し続ける。
        while (ReferenceCount > 1) await Task.Delay(50);
    }

    private static bool OwnsClipboard()
    {
        var owner = GetClipboardOwner();
        if (owner == 0) return false;
        GetWindowThreadProcessId(owner, out var process);
        return process == Environment.ProcessId;
    }

    public void Dispose()
    {
        if (count == 0) return;
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Shellのスレッド参照は作成したSTAで解放してください。");
        SHSetThreadRef(0);
        if (reference != 0) { Marshal.Release(reference); reference = 0; }
        // 強制終了などで参照が残る場合は、Shellが使うカウンターを解放しない。
        if (Marshal.ReadInt32(count) == 0) Marshal.FreeCoTaskMem(count);
        count = 0;
    }

    [DllImport("shlwapi.dll")] private static extern int SHCreateThreadRef(nint count, out nint reference);
    [DllImport("shlwapi.dll")] private static extern int SHSetThreadRef(nint reference);
    [DllImport("ole32.dll")] private static extern int OleFlushClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
}
