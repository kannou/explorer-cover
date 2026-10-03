using System.IO;
using System.Runtime.CompilerServices;
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
        await Check("別STAのShellコピーで複数ファイルとフォルダーを保持し、終了待ち中もUIを処理する", async () =>
        {
            var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "shell-copy-" + Guid.NewGuid().ToString("N"));
            var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            var folder = Directory.CreateDirectory(Path.Combine(source, "日本語 フォルダー")).FullName;
            var a = Path.Combine(source, "a.bin"); var b = Path.Combine(source, "b.txt");
            File.WriteAllBytes(a, new byte[8 * 1024 * 1024]); File.WriteAllText(b, "copy test");
            File.WriteAllText(Path.Combine(folder, "child.txt"), "child");
            var operation = ShellCopyOperation.Start([a, b, folder], destination);
            var waiting = ShellCopyOperation.WaitForIdleAsync();
            var responsive = false;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => responsive = true);
            Assert(responsive, "別STAへのコピーがUIを塞いだ");
            await waiting.WaitAsync(TimeSpan.FromSeconds(15));
            Assert(await operation, "コピーが中止された");
            Assert(File.Exists(a) && File.Exists(b) && Directory.Exists(folder), "コピー元が削除された");
            Assert(File.ReadAllBytes(Path.Combine(destination, "a.bin")).SequenceEqual(File.ReadAllBytes(a)), "バイナリーの内容が不一致");
            Assert(File.ReadAllText(Path.Combine(destination, "b.txt")) == "copy test" && File.ReadAllText(Path.Combine(destination, "日本語 フォルダー", "child.txt")) == "child", "複数コピーの内容が不一致");
        });
        await Check("WSLのサーバー名だけを識別し、他のUNCは含めない", () =>
        {
            Assert(ShellCopyOperation.IsWslPath(@"\\WSL.LOCALHOST\Ubuntu\tmp") && ShellCopyOperation.IsWslPath(@"\\wsl$\Ubuntu\tmp"), "WSL別名を識別しない");
            Assert(!ShellCopyOperation.IsWslPath(@"\\wsl.localhost.example\share") && !ShellCopyOperation.IsWslPath(@"D:\wsl.localhost\test"), "別サーバーやローカルパスを誤認した");
            return Task.CompletedTask;
        });
        await Check("参照されなくなったShellデータのRCWを終了待ちで解放する", async () =>
        {
            using var lifetime = new ShellThreadLifetime();
            var root = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts", "shell-data-" + Guid.NewGuid().ToString("N"))).FullName;
            var path = Path.Combine(root, "source.txt"); File.WriteAllText(path, "data");
            var weak = CreateUnreferencedData(path);
            Assert(lifetime.ReferenceCount > 1, "検証用のShellデータがスレッドを参照していない");
            await lifetime.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert(lifetime.ReferenceCount == 1, "到達できないRCWがShellを参照し続けている");
            Assert(!weak.Native.IsAlive && !weak.Wrapper.IsAlive, "Shellデータが回収されていない");
        });
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Native, WeakReference Wrapper) CreateUnreferencedData(string path)
    {
        var iid = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out var item));
        nint pointer = 0;
        try
        {
            var handler = new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5");
            iid = typeof(System.Runtime.InteropServices.ComTypes.IDataObject).GUID;
            item.BindToHandler(0, ref handler, ref iid, out pointer);
            var native = Marshal.GetObjectForIUnknown(pointer);
            var data = new DataObject(native);
            GC.KeepAlive(data);
            return (new(native), new(data));
        }
        finally { if (pointer != 0) Marshal.Release(pointer); Marshal.ReleaseComObject(item); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("shlwapi.dll")] private static extern int SHGetThreadRef(out nint reference);
}
