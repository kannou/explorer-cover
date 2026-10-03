using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ExplorerCover;
using ExplorerCover.Core;
using ExplorerCover.Shell;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

// マウス・キー入力は使わない。不可視のHWNDで座標変換し、専用ファイルだけを操作する。
internal static class Program
{
    private static int count, failures;
    private const DragDropEffects Allowed = DragDropEffects.Copy | DragDropEffects.Move;
    private static readonly Point Screen = new(100, 100);
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
        Console.WriteLine($"{count - failures}/{count} passed");
        return failures == 0 ? 0 : 1;
    }
    private static async Task Check(string name, Func<Task> action)
    {
        count++;
        try { await action(); Console.WriteLine("PASS: " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL: " + name + ": " + ex); }
    }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static DataObject Files(params string[] paths)
    {
        var data = new DataObject(); var files = new StringCollection(); files.AddRange(paths);
        data.SetFileDropList(files); return data;
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition()) { if (DateTime.UtcNow >= deadline) throw new TimeoutException(); await Task.Delay(25); }
    }
    private sealed class FakeTarget : IShellDropTarget
    {
        public int Enters, Overs, Leaves, Drops;
        public uint LastKeys, LastAllowed;
        public ComDataObject? Data;
        public bool FailDrop, CancelDrop;
        public uint Effect = (uint)DragDropEffects.Copy;
        public void DragEnter(ComDataObject data, uint keys, DropPoint point, ref uint effect)
        { Enters++; Data = data; LastKeys = keys; LastAllowed = effect; effect = Effect; }
        public void DragOver(uint keys, DropPoint point, ref uint effect)
        { Overs++; LastKeys = keys; LastAllowed = effect; effect = Effect; }
        public void DragLeave() => Leaves++;
        public int Drop(ComDataObject data, uint keys, DropPoint point, ref uint effect)
        {
            Drops++; Data = data; LastKeys = keys; LastAllowed = effect;
            if (FailDrop) throw new COMException("検証用エラー");
            effect = Effect;
            return CancelDrop ? 0x40101 : 0;
        }
    }
    private sealed class Harness : IDisposable
    {
        public readonly PaneState State = new(@"D:\selected");
        public readonly TabState Other;
        public readonly List<(string Path, FakeTarget Target)> Targets = [];
        public readonly List<string> Errors = [];
        public readonly TabStrip Strip;
        public readonly RadioButton First, Second;
        private readonly HwndSource source;
        public Harness(Func<string, ShellFolderDropTarget>? factory = null, double width = 500, Func<string[], string, Task<bool>>? startCopy = null)
        {
            Other = State.AddTab(@"D:\other");
            Strip = new(State, new MouseSettings(), _ => throw new Exception("終了操作が発火した"),
                () => throw new Exception("追加操作が発火した"), "検証", Errors.Add, factory ?? (path =>
                { var target = new FakeTarget(); Targets.Add((path, target)); return new(target); }), startCopy);
            First = new() { Content = "selected", Width = 100, Height = 30 };
            Second = new() { Content = "other", Width = 100, Height = 30 };
            Strip.Add(State.SelectedTab, First); Strip.Add(Other, Second);
            source = new(new HwndSourceParameters("tab drop tests") { Width = (int)width, Height = 100, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = Strip;
            Strip.Measure(new Size(width, 100)); Strip.Arrange(new Rect(0, 0, width, 100)); Strip.UpdateLayout();
        }
        public Point At(RadioButton button) => button.TranslatePoint(new Point(50, 15), Strip);
        public DragEventArgs Send(RoutedEvent routedEvent, Point point, DataObject? data = null, DragDropKeyStates keys = DragDropKeyStates.LeftMouseButton, DragDropEffects allowed = Allowed)
        {
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data ?? Files(@"D:\source.txt"), keys, allowed, Strip, point], null)!;
            args.RoutedEvent = routedEvent;
            Strip.RaiseEvent(args);
            Assert(args.Handled, "イベントが処理されていない");
            return args;
        }
        public void Dispose() { Strip.Dispose(); source.Dispose(); }
    }
    private static async Task Run()
    {
        await Check("非選択タブにドロップしても選択・履歴・タブ順を維持する", () => Sync(() =>
        {
            using var h = new Harness(); var selected = h.State.SelectedTab;
            h.Other.NavigationSucceeded(@"D:\destination"); h.Other.AddressText = @"D:\editing";
            var data = Files(@"D:\source.txt");
            var enter = h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second), data);
            Assert(h.Strip.Children.OfType<Border>().Last().Visibility == Visibility.Visible, "宛先を強調表示していない");
            var drop = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second), data);
            Assert(h.Strip.Children.OfType<Border>().Last().Visibility == Visibility.Hidden, "ドロップ後も強調表示が残る");
            var t = h.Targets.Single();
            Assert(t.Path == @"D:\destination" && t.Target.Enters == 1 && t.Target.Drops == 1 && t.Target.Leaves == 0, "現在地へ一度だけドロップできない");
            Assert(ReferenceEquals(t.Target.Data, data), "元のデータを保持していない");
            Assert(enter.Effects == DragDropEffects.Copy && drop.Effects == DragDropEffects.Copy, "Shellの効果を返していない");
            Assert(h.State.SelectedTab == selected && h.State.Tabs[1] == h.Other && h.Other.AddressText == @"D:\editing" && h.Other.History.Entries.Count == 1, "タブの状態を変更した");
        }));
        await Check("見出し間の移動・空白・バー外では古いドロップ先を解除する", () => Sync(() =>
        {
            using var h = new Harness();
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.First));
            h.Send(DragDrop.PreviewDragOverEvent, h.At(h.Second));
            Assert(h.Targets[0].Target.Leaves == 1 && h.Targets[1].Path == h.Other.InitialPath, "別タブへ切り替えられない");
            var blank = h.Send(DragDrop.PreviewDropEvent, new Point(300, 15));
            Assert(blank.Effects == DragDropEffects.None && h.Targets.All(t => t.Target.Drops == 0) && h.Targets[1].Target.Leaves == 1, "空白にドロップした");
            var outside = h.Send(DragDrop.PreviewDropEvent, new Point(-1, 15));
            Assert(outside.Effects == DragDropEffects.None && h.Targets.Count == 2, "バー外を受理した");
        }));
        await Check("Ctrl・Shiftとソースの許可効果をShellに伝える", () => Sync(() =>
        {
            using var h = new Harness();
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second), keys: DragDropKeyStates.ControlKey | DragDropKeyStates.LeftMouseButton);
            var target = h.Targets.Single().Target;
            Assert(target.LastKeys == 9 && target.LastAllowed == (uint)Allowed, "Ctrlと許可効果が不正");
            target.Effect = (uint)DragDropEffects.Move;
            var over = h.Send(DragDrop.PreviewDragOverEvent, h.At(h.Second), keys: DragDropKeyStates.ShiftKey);
            Assert(over.Effects == DragDropEffects.Move && target.LastKeys == 4, "Shiftを伝えていない");
            var rejected = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second), allowed: DragDropEffects.Copy);
            Assert(rejected.Effects == DragDropEffects.None && target.Drops == 0, "許可されていない移動を実行した");
        }));
        await Check("Escape相当のDragLeaveは見出し内でも取消し、再進入できる", () => Sync(() =>
        {
            using var h = new Harness();
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second));
            h.Send(DragDrop.PreviewDragLeaveEvent, h.At(h.Second));
            Assert(h.Targets.Single().Target.Leaves == 1 && h.Targets.Single().Target.Drops == 0, "取消で解除されていない");
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second));
            Assert(h.Targets.Count == 2, "再進入できない");
        }));
        await Check("受信中のタブ閉鎖とDisposeはセッションを一度だけ解放する", () => Sync(() =>
        {
            using var h = new Harness();
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second));
            h.State.CloseTab(h.Other); h.Strip.Remove(h.Other);
            Assert(h.Targets.Single().Target.Leaves == 1, "閉鎖時に解除されていない");
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.First)); h.Strip.Dispose(); h.Strip.Dispose();
            Assert(h.Targets[1].Target.Leaves == 1, "二重解放または解除漏れ");
            Assert(h.Send(DragDrop.PreviewDropEvent, h.At(h.First)).Effects == DragDropEffects.None, "破棄後に受理した");
        }));
        await Check("無効な宛先とShellの失敗はエラー表示し、ファイル操作を繰り返さない", () => Sync(() =>
        {
            using var missing = new Harness(_ => throw new COMException("宛先がありません"));
            missing.Send(DragDrop.PreviewDragEnterEvent, missing.At(missing.Second));
            missing.Send(DragDrop.PreviewDragOverEvent, missing.At(missing.Second));
            Assert(missing.Errors.Count == 1, "同じエラーを繰り返した");
            using var h = new Harness();
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second)); h.Targets.Single().Target.FailDrop = true;
            var failed = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second));
            Assert(failed.Effects == DragDropEffects.None && h.Errors.Single().StartsWith("ドロップできません:"), "失敗を正常扱いした");
        }));
        await Check("ドラッグ途中のタブ現在地変更を反映する", () => Sync(() =>
        {
            using var h = new Harness();
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second)); h.Other.NavigationSucceeded(@"D:\changed");
            h.Send(DragDrop.PreviewDropEvent, h.At(h.Second));
            Assert(h.Targets[0].Target.Leaves == 1 && h.Targets[0].Target.Drops == 0 && h.Targets[1].Path == @"D:\changed" && h.Targets[1].Target.Drops == 1, "旧宛先へ操作した");
        }));
        await Check("Shellの取消結果をドロップ元へ返す", () => Sync(() =>
        {
            using var h = new Harness();
            h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second)); h.Targets.Single().Target.CancelDrop = true;
            var result = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second));
            Assert(result.Effects == DragDropEffects.None && h.Errors.Count == 0, "取消を成功またはエラー扱いした");
        }));
        await Check("ファイルドラッグ中もバー端でスクロールできる", async () =>
        {
            using var h = new Harness(width: 150);
            var scroll = h.Strip.Children.OfType<ScrollViewer>().Single();
            h.Send(DragDrop.PreviewDragEnterEvent, new Point(145, 15));
            await Until(() => scroll.HorizontalOffset > 0);
            h.Send(DragDrop.PreviewDragLeaveEvent, new Point(145, 15));
            var offset = scroll.HorizontalOffset; await Task.Delay(120);
            Assert(scroll.HorizontalOffset == offset, "取消後もスクロールしている");
        });

        await Check("WSL宛てのコピーは転送の完了前に返り、確定した宛先で実行する", async () =>
        {
            var completion = new TaskCompletionSource<bool>();
            string[]? sources = null; string? destination = null;
            using var h = new Harness(startCopy: (paths, path) => { sources = paths; destination = path; return completion.Task; });
            h.Other.NavigationSucceeded(@"\\wsl.localhost\Ubuntu-24.04\tmp\test");
            var result = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second), Files(@"D:\file.txt", @"D:\folder"), DragDropKeyStates.ControlKey);
            h.Other.NavigationSucceeded(@"D:\changed");
            Assert(result.Effects == DragDropEffects.Copy && !completion.Task.IsCompleted, "完了までドロップを待っている");
            Assert(destination == @"\\wsl.localhost\Ubuntu-24.04\tmp\test" && sources!.SequenceEqual(new[] { @"D:\file.txt", @"D:\folder" }), "宛先または複数のコピー元が不正");
            Assert(h.Targets.Single().Target.Drops == 0 && h.Targets.Single().Target.Leaves == 1, "UIで転送またはドラッグの解除漏れ");
            completion.SetResult(false); await Task.Yield();
            Assert(h.Errors.Count == 0, "取消をエラー表示した");
        });
        await Check("WSL別名とWSLからのコピーも別STAへ渡す", () => Sync(() =>
        {
            foreach (var (source, destination) in new[] { (@"D:\file.txt", @"\\wsl$\Ubuntu\tmp"), (@"\\wsl.localhost\Ubuntu\tmp\file.txt", @"D:\other") })
            {
                var called = false;
                using var h = new Harness(startCopy: (_, _) => { called = true; return Task.FromResult(true); });
                h.Other.NavigationSucceeded(destination);
                var result = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second), Files(source), DragDropKeyStates.ControlKey);
                Assert(called && result.Effects == DragDropEffects.Copy && h.Targets.Single().Target.Drops == 0, "WSLコピーの経路が不正");
            }
        }));
        await Check("WSLでも移動・右ドラッグは元のShellの結果を返す", () => Sync(() =>
        {
            foreach (var keys in new[] { DragDropKeyStates.ShiftKey, DragDropKeyStates.RightMouseButton })
            {
                using var h = new Harness(startCopy: (_, _) => throw new Exception("移動または右ドラッグを先行受理した"));
                h.Other.NavigationSucceeded(@"\\wsl.localhost\Ubuntu\tmp");
                h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second));
                if (keys == DragDropKeyStates.ShiftKey) h.Targets.Single().Target.Effect = (uint)DragDropEffects.Move;
                var result = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second), keys: keys);
                Assert(h.Targets.Single().Target.Drops == 1 && result.Effects != DragDropEffects.None, "Shellに委譲していない");
            }
        }));
        await Check("別STAコピーの失敗は元データの削除を指示せず、エラー表示する", async () =>
        {
            var completion = new TaskCompletionSource<bool>();
            using var h = new Harness(startCopy: (_, _) => completion.Task);
            h.Other.NavigationSucceeded(@"\\wsl$\Ubuntu\tmp");
            var result = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second), keys: DragDropKeyStates.ControlKey);
            completion.SetException(new IOException("コピー失敗"));
            await Until(() => h.Errors.Count == 1);
            Assert(result.Effects == DragDropEffects.Copy && h.Errors.Single().StartsWith("コピーできません:"), "結果または失敗表示が不正");
        });

        var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "tab-drop-" + Guid.NewGuid().ToString("N"));
        var from = Directory.CreateDirectory(Path.Combine(root, "移動元")).FullName;
        var to = Directory.CreateDirectory(Path.Combine(root, "宛先")).FullName;
        Console.WriteLine("検証ファイル: " + root);
        await Check("ShellのIDataObjectをWPFで受け、非選択タブへの実コピーを完了する", async () =>
        {
            var path = Path.Combine(from, "Explorer形式.txt"); File.WriteAllText(path, "Shell data");
            var iid = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out var item));
            nint pointer = 0; ComDataObject? nativeData = null;
            try
            {
                var handler = new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5");
                iid = typeof(ComDataObject).GUID;
                item.BindToHandler(0, ref handler, ref iid, out pointer);
                nativeData = (ComDataObject)Marshal.GetObjectForIUnknown(pointer);
                var data = new DataObject(nativeData);
                using var h = new Harness(ShellFolderDropTarget.Create); h.Other.NavigationSucceeded(to);
                var selected = h.State.SelectedTab;
                var enter = h.Send(DragDrop.PreviewDragEnterEvent, h.At(h.Second), data, DragDropKeyStates.LeftMouseButton | DragDropKeyStates.ControlKey);
                Assert(enter.Effects == DragDropEffects.Copy, "Shellデータを受理できない");
                var drop = h.Send(DragDrop.PreviewDropEvent, h.At(h.Second), data, DragDropKeyStates.ControlKey);
                Assert(drop.Effects == DragDropEffects.Copy, "実コピーの結果が不正");
                var copy = Path.Combine(to, "Explorer形式.txt"); await Until(() => File.Exists(copy));
                Assert(File.ReadAllText(copy) == "Shell data" && File.Exists(path) && h.State.SelectedTab == selected, "内容・コピー元・選択が不正");
            }
            finally
            {
                if (nativeData != null) Marshal.ReleaseComObject(nativeData);
                if (pointer != 0) Marshal.Release(pointer);
                Marshal.ReleaseComObject(item);
            }
        });
        foreach (var (name, keys, expected) in new[]
        {
            ("Ctrlで複数ファイルをコピー", DragDropKeyStates.ControlKey, DragDropEffects.Copy),
            ("Shiftで複数ファイルを移動", DragDropKeyStates.ShiftKey, DragDropEffects.Move),
            ("修飾キーなしの同一ボリューム移動", DragDropKeyStates.None, DragDropEffects.Move)
        })
            await Check(name, async () =>
            {
                var names = new[] { Guid.NewGuid().ToString("N") + " 日本語.txt", Guid.NewGuid().ToString("N") + ".txt" };
                foreach (var file in names) File.WriteAllText(Path.Combine(from, file), "ドロップ検証");
                var data = Files(names.Select(file => Path.Combine(from, file)).ToArray());
                using var target = ShellFolderDropTarget.Create(to);
                Assert(target.Enter(data, keys | DragDropKeyStates.LeftMouseButton, Screen, Allowed) == expected, "Shellの開始判定が不正");
                Assert(target.Over(keys, Screen, Allowed) == expected, "Shellの修飾キー判定が不正");
                Assert(target.Drop(data, keys, Screen, Allowed) == expected, "Shellのドロップ結果が不正");
                await Until(() => names.All(file => File.Exists(Path.Combine(to, file))) && (expected != DragDropEffects.Move || names.All(file => !File.Exists(Path.Combine(from, file)))));
                Assert(names.All(file => File.ReadAllText(Path.Combine(to, file)) == "ドロップ検証"), "ファイル内容が不一致");
                if (expected == DragDropEffects.Copy) Assert(names.All(file => File.Exists(Path.Combine(from, file))), "コピーで元ファイルを失った");
            });
        await Check("フォルダーを内容ごとコピーする", async () =>
        {
            var folder = Directory.CreateDirectory(Path.Combine(from, "子フォルダー")).FullName;
            File.WriteAllText(Path.Combine(folder, "内容.txt"), "folder content");
            var data = Files(folder);
            using var target = ShellFolderDropTarget.Create(to);
            target.Enter(data, DragDropKeyStates.ControlKey | DragDropKeyStates.LeftMouseButton, Screen, Allowed);
            Assert(target.Drop(data, DragDropKeyStates.ControlKey, Screen, Allowed) == DragDropEffects.Copy, "フォルダーをコピーできない");
            var copy = Path.Combine(to, "子フォルダー", "内容.txt"); await Until(() => File.Exists(copy));
            Assert(File.ReadAllText(copy) == "folder content" && File.Exists(Path.Combine(folder, "内容.txt")), "フォルダー内容が不一致");
        });
        await Check("取消したShellセッションはファイルを操作しない", async () =>
        {
            var path = Path.Combine(from, "cancel.txt"); File.WriteAllText(path, "cancel");
            using (var target = ShellFolderDropTarget.Create(to)) target.Enter(Files(path), DragDropKeyStates.ControlKey | DragDropKeyStates.LeftMouseButton, Screen, Allowed);
            await Task.Delay(150);
            Assert(File.Exists(path) && !File.Exists(Path.Combine(to, "cancel.txt")), "取消なのに操作された");
        });
        await Check("文字列のみのデータと削除済み・ファイルの宛先を拒否する", () => Sync(() =>
        {
            using var target = ShellFolderDropTarget.Create(to);
            Assert(target.Enter(new DataObject(DataFormats.UnicodeText, "text"), DragDropKeyStates.ControlKey | DragDropKeyStates.LeftMouseButton, Screen, Allowed) == DragDropEffects.None, "テキストを受理した");
            foreach (var path in new[] { Path.Combine(root, "missing"), Path.Combine(from, "cancel.txt") })
            {
                var rejected = false;
                try { using var invalid = ShellFolderDropTarget.Create(path); }
                catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException) { rejected = true; }
                Assert(rejected, "不正な宛先を受理した");
            }
        }));
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);
}
