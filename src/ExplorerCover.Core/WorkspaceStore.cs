using System.Text;
using System.Text.Json;

namespace ExplorerCover.Core;

// 呼び出し元が作業スレッドで直列に使う。保存中も既存JSONは完全な状態で残す。
public sealed class WorkspaceStore(string path) : IDisposable
{
    private const int MaxBytes = 4 * 1024 * 1024;
    private FileStream? ownership;
    public bool CanSave { get; private set; }
    public (WorkspaceSnapshot? Snapshot, string? Warning) Load()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            CanSave = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return (null, "状態の保存先を使用できません。このウィンドウでは保存しません: " + ex.Message); }
        try { return (Read(path), null); }
        catch (NotSupportedException ex) { CanSave = false; return (null, ex.Message + " 元のファイルを保護するため保存しません。"); }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            try
            {
                // 破損ファイルを残し、前回の正常な保存へ戻す。バックアップも不正なら初期状態。
                File.Copy(path, path + ".invalid-" + Guid.NewGuid().ToString("N"));
                WorkspaceSnapshot? backup = null;
                try { backup = Read(path + ".bak"); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or FormatException or NotSupportedException) { }
                return (backup, backup == null ? "状態ファイルを退避し、初期状態で起動しました。" : "状態ファイルを退避し、前回のバックアップを復元しました。");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { CanSave = false; return (null, "状態ファイルを保護するため保存しません: " + error.Message); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { CanSave = false; return (null, "状態を読み込めないため、この起動では保存しません: " + ex.Message); }
    }
    private static WorkspaceSnapshot? Read(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes) throw new FormatException("状態ファイルが大きすぎます。");
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return WorkspaceSnapshot.FromJson(reader.ReadToEnd());
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public void Save(string json)
    {
        if (!CanSave) throw new InvalidOperationException("このウィンドウでは状態を保存できません。");
        WorkspaceSnapshot.FromJson(json);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxBytes) throw new IOException("状態ファイルの保存上限を超えました。");
        AtomicFile.Write(path, json);
    }
    public void Dispose() { CanSave = false; ownership?.Dispose(); ownership = null; }
}
