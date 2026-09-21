using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using ExplorerCover.Core;

namespace ExplorerCover;

internal sealed class QuickLookClient(QuickLookSettings settings, string? pipeNameOverride = null,
    Func<ProcessStartInfo?>? findStart = null, Action<ProcessStartInfo>? startProcess = null)
{
    private const string StoreFamily = "21090PaddyXu.QuickLook_egxr34yet59cg";

    public Task PreviewAsync(string path, Func<bool> stillCurrent, CancellationToken cancellation) =>
        SendAsync(path, stillCurrent, cancellation, false);

    // Switchは表示中のプレビューだけを更新する。閉じたウィンドウを再表示しない。
    public Task SwitchAsync(string path, Func<bool> stillCurrent, CancellationToken cancellation) =>
        SendAsync(path, stillCurrent, cancellation, true);

    private async Task SendAsync(string path, Func<bool> stillCurrent, CancellationToken cancellation, bool switchOnly)
    {
        if (path.IndexOfAny(['|', '\r', '\n']) >= 0) throw new IOException("このパスはQuickLookに送信できません。");
        // ファイル・起動先の確認もUIスレッドで行わない。
        await Task.Run(async () =>
        {
            if (!File.Exists(path)) throw new IOException("選択ファイルが存在しないか、アクセスできません。");
            if (!switchOnly && settings.ExecutablePath != null && !File.Exists(settings.ExecutablePath))
                throw new IOException("設定したQuickLook.exeが見つかりません。設定画面の起動先を確認してください。");
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new IOException("ユーザーを識別できません。");
            var pipeName = pipeNameOverride ?? "QuickLook.App.Pipe." + sid;
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            cancellation.ThrowIfCancellationRequested();
            // 接続成立とキャンセルが競合しても、成立した接続は必ず一行送って閉じる。
            try { await pipe.ConnectAsync(250, CancellationToken.None); }
            catch (TimeoutException)
            {
                cancellation.ThrowIfCancellationRequested();
                if (switchOnly) return; // 選択変更だけではQuickLookを起動しない。
                if (!stillCurrent()) return;
                var start = (findStart ?? FindStartInfo)() ?? throw new IOException("QuickLookが見つかりません。起動するか設定画面で実行ファイルを指定してください。");
                cancellation.ThrowIfCancellationRequested();
                if (startProcess != null) startProcess(start);
                else { using var process = Process.Start(start); }
                cancellation.ThrowIfCancellationRequested();
                try { await pipe.ConnectAsync(5000, CancellationToken.None); }
                catch (TimeoutException) { cancellation.ThrowIfCancellationRequested(); throw; }
            }
            var message = "";
            try
            {
                cancellation.ThrowIfCancellationRequested();
                // 呼び出し側でDispatcherへ戻してタブ・フォーカスを確認する。
                if (!stillCurrent()) return;
                cancellation.ThrowIfCancellationRequested();
                var command = switchOnly ? "Switch" : "Toggle";
                message = $"QuickLook.App.PipeMessages.{command}|" + path;
            }
            finally
            {
                // QuickLook 4.5はEOF(null)を処理できず受信ループが止まる。
                // 取り消す場合も無操作の空行を送り、キャンセルで送信を中断しない。
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var bytes = Encoding.UTF8.GetBytes(message + "\n");
                try { await pipe.WriteAsync(bytes, timeout.Token); }
                catch (OperationCanceledException)
                { throw new TimeoutException("QuickLookへの送信がタイムアウトしました。"); }
            }
            DiagnosticLog.Write(switchOnly ? "QuickLook selection switched" : "QuickLook request sent");
        }, cancellation);
    }

    private ProcessStartInfo? FindStartInfo()
    {
        if (settings.ExecutablePath != null) return new(settings.ExecutablePath) { UseShellExecute = true };
        using var store = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Families\" + StoreFamily);
        if (store != null)
        {
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            info.ArgumentList.Add("shell:AppsFolder\\" + StoreFamily + "!Main");
            return info;
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            var path = Path.Combine(root, "QuickLook", "QuickLook.exe");
            if (File.Exists(path)) return new(path) { UseShellExecute = true };
        }
        return null;
    }
}
