using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ExplorerCover.Shell;

internal static class Program
{
    private static int failures, checks;
    [STAThread]
    private static int Main()
    {
        Marshal.ThrowExceptionForHR(OleInitialize(0));
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(async () =>
        {
            try { await Run(); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        OleUninitialize();
        Console.WriteLine($"{checks - failures}/{checks} passed");
        return failures == 0 ? 0 : 1;
    }
    private static async Task Check(string name, Func<Task> run)
    {
        checks++;
        try { await run(); Console.WriteLine("PASS: " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL: " + name + ": " + ex); }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Run()
    {
        await Check("Shellが参照するSTAを登録し、子処理の参照を数える", () =>
        {
            using var lifetime = new ShellThreadLifetime();
            Assert(lifetime.ReferenceCount == 1, "初期参照数が不正");
            Marshal.ThrowExceptionForHR(SHGetThreadRef(out var reference));
            try { Assert(lifetime.ReferenceCount == 2, "Shellの追加参照が反映されない"); }
            finally { Marshal.Release(reference); }
            Assert(lifetime.ReferenceCount == 1, "Shellの参照を解放できない");
            return Task.CompletedTask;
        });
        await Check("非同期処理がない場合は終了を待たず、スレッド参照を解除する", async () =>
        {
            var lifetime = new ShellThreadLifetime();
            var waiting = lifetime.WaitForIdleAsync();
            Assert(waiting.IsCompleted, "不要な終了待ちが発生した");
            await waiting;
            lifetime.Dispose(); lifetime.Dispose();
            var hr = SHGetThreadRef(out var reference);
            if (reference != 0) Marshal.Release(reference);
            Assert(hr < 0, "解放したスレッド参照が残る");
        });
        await Check("Shellの処理中はDispatcherを動かしたまま終了を待ち、完了後に解放できる", async () =>
        {
            using var lifetime = new ShellThreadLifetime();
            Marshal.ThrowExceptionForHR(SHGetThreadRef(out var reference));
            var waiting = lifetime.WaitForIdleAsync();
            try
            {
                Assert(!waiting.IsCompleted, "Shellの処理中に終了した");
                var responsive = false;
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => responsive = true);
                Assert(responsive && !waiting.IsCompleted, "終了待ちがUIを塞いだ");
            }
            finally { Marshal.Release(reference); }
            await waiting.WaitAsync(TimeSpan.FromSeconds(3));
        });
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("shlwapi.dll")] private static extern int SHGetThreadRef(out nint reference);
}
