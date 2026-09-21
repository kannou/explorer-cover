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
    await Check("送信直前の選択変更でもQuickLook 4.5の受信ループを止めない", async () =>
    {
        var name = PipeName(); using var pipe = Server(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        async Task Receive()
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            for (var i = 0; i < 2; i++)
            {
                await pipe.WaitForConnectionAsync(timeout.Token);
                var line = await reader.ReadLineAsync(timeout.Token);
                // QuickLook 4.5と同様、nullを防御せず解析する。空行は無操作として受理する。
                var fields = line!.Split('|');
                if (i == 0) Assert(fields.Length == 1);
                else Assert(line == "QuickLook.App.PipeMessages.Toggle|" + file);
                pipe.Disconnect();
            }
        }
        var receive = Receive();
        var client = new QuickLookClient(new(), name, () => throw new Exception("再起動しました"));
        await client.SwitchAsync(file, () => false, CancellationToken.None);
        // 受信側の例外を先に確認し、後続接続のタイムアウトに隠さない。
        await Task.WhenAny(receive, Task.Delay(100));
        if (receive.IsCompleted) await receive;
        await client.PreviewAsync(file, () => true, CancellationToken.None);
        await receive;
    });
    await Check("接続後のキャンセル・選択確認の例外でもEOFを送らない", async () =>
    {
        foreach (var cancel in new[] { true, false })
        {
            var name = PipeName(); using var pipe = Server(name); var read = Read(pipe);
            using var cancellation = new CancellationTokenSource();
            var client = new QuickLookClient(new(), name);
            bool Current()
            {
                if (!cancel) throw new InvalidOperationException("選択確認が失敗");
                cancellation.Cancel();
                return true;
            }
            if (cancel) await Throws<OperationCanceledException>(() => client.PreviewAsync(file, Current, cancellation.Token));
            else await Throws<InvalidOperationException>(() => client.PreviewAsync(file, Current, cancellation.Token));
            Assert(await read == "");
        }
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
