using System.Diagnostics;
using System.IO.Pipes;
using ExplorerCover;

// 実際のQuickLookやユーザーのプレビューを止めず、独立した通信口で障害経路を確認する。
var directory = Path.Combine(Path.GetTempPath(), "explorer-cover-quicklook-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var file = Path.Combine(directory, "日本語 sample.txt");
File.WriteAllText(file, "sample");
var failures = 0; var count = 0;
async Task Check(string name, Func<Task> test)
{
    count++;
    try { await test(); Console.WriteLine("PASS: " + name); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL: {name}: {ex}"); }
}
void Assert(bool condition) { if (!condition) throw new Exception("期待した結果ではありません。"); }
async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception(typeof(T).Name + "が発生しませんでした。");
}
string PipeName() => "explorer-cover-test-" + Guid.NewGuid().ToString("N");
NamedPipeServerStream Server(string name) => new(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
async Task<string?> Read(NamedPipeServerStream pipe)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    await pipe.WaitForConnectionAsync(timeout.Token);
    using var reader = new StreamReader(pipe);
    return await reader.ReadLineAsync(timeout.Token);
}
try
{
    await Check("起動済みの通信口へ日本語・空白パスを一度送信", async () =>
    {
        var name = PipeName(); using var pipe = Server(name); var read = Read(pipe);
        var client = new QuickLookClient(new(), name, () => throw new Exception("起動処理を呼びました"));
        await client.PreviewAsync(file, () => true, CancellationToken.None);
        Assert(await read == "QuickLook.App.PipeMessages.Toggle|" + file);
    });
    await Check("未起動なら一度起動して接続を再試行", async () =>
    {
        var name = PipeName(); Task<string?>? read = null; var starts = 0;
        var client = new QuickLookClient(new(), name, () => new ProcessStartInfo("fake.exe"), _ =>
        {
            starts++;
            read = Task.Run(async () => { await Task.Delay(150); using var pipe = Server(name); return await Read(pipe); });
        });
        await client.PreviewAsync(file, () => true, CancellationToken.None);
        Assert(starts == 1 && read != null && await read == "QuickLook.App.PipeMessages.Toggle|" + file);
    });
    await Check("選択変更はToggleではなくSwitchを送信し未起動なら起動しない", async () =>
    {
        var name = PipeName(); using var pipe = Server(name); var read = Read(pipe);
        var client = new QuickLookClient(new(), name, () => throw new Exception("選択変更で起動しました"));
        await client.SwitchAsync(file, () => true, CancellationToken.None);
        Assert(await read == "QuickLook.App.PipeMessages.Switch|" + file);
        await new QuickLookClient(new(), PipeName(), () => throw new Exception("未起動から起動しました"))
            .SwitchAsync(file, () => true, CancellationToken.None);
    });
    await Check("送信直前に選択が変わったSwitchは破棄する", async () =>
    {
        var name = PipeName(); using var pipe = Server(name); var read = Read(pipe);
        await new QuickLookClient(new(), name).SwitchAsync(file, () => false, CancellationToken.None);
        Assert(await read == null);
    });
    await Check("未検出・起動失敗・接続タイムアウトを通知可能な例外にする", async () =>
    {
        await Throws<IOException>(() => new QuickLookClient(new(), PipeName(), () => null).PreviewAsync(file, () => true, CancellationToken.None));
        await Throws<System.ComponentModel.Win32Exception>(() => new QuickLookClient(new(), PipeName(), () => new("fake.exe"), _ => throw new System.ComponentModel.Win32Exception()).PreviewAsync(file, () => true, CancellationToken.None));
        await Throws<TimeoutException>(() => new QuickLookClient(new(), PipeName(), () => new("fake.exe"), _ => { }).PreviewAsync(file, () => true, CancellationToken.None));
    });
    await Check("対象変更後は起動せず、待機中の終了要求で中断", async () =>
    {
        await new QuickLookClient(new(), PipeName(), () => throw new Exception("対象変更後に起動"))
            .PreviewAsync(file, () => false, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var client = new QuickLookClient(new(), PipeName(), () => new("fake.exe"), _ => cancellation.Cancel());
        await Throws<OperationCanceledException>(() => client.PreviewAsync(file, () => true, cancellation.Token));
    });
    await Check("消えたファイル・不正な起動先・通信区切り文字を拒否", async () =>
    {
        var client = new QuickLookClient(new(), PipeName());
        await Throws<IOException>(() => client.PreviewAsync(Path.Combine(directory, "missing.txt"), () => true, CancellationToken.None));
        await Throws<IOException>(() => client.PreviewAsync(file + "\n", () => true, CancellationToken.None));
        await Throws<IOException>(() => new QuickLookClient(new(Path.Combine(directory, "missing.exe")), PipeName()).PreviewAsync(file, () => true, CancellationToken.None));
    });
}
finally { File.Delete(file); Directory.Delete(directory); }
Console.WriteLine($"{count - failures}/{count} passed");
return failures == 0 ? 0 : 1;
