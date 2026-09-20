using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ExplorerCover.Shell;

public sealed class ExplorerHost : HwndHost
{
    private IExplorerBrowser? browser;
    private BrowserSite? site;
    private uint cookie;
    private bool advised;
    private bool initialized;
    private nint child;
    private string pendingPath;
    private CancellationTokenSource? preparation;
    private long requestVersion;
    private bool shellNavigating;
    private bool submitting;
    private readonly Func<string, CancellationToken, Task<byte[]>> resolvePath;
    private readonly TimeSpan preparationTimeout;
    public bool CanNavigate => browser != null && !shellNavigating;
    public bool IsNavigating { get; private set; } = true;
    public event Action? NavigationStateChanged;
    public string CurrentPath { get; private set; } = "";
    public event Action<string>? Navigated;
    public event Action<string>? Error;
    public event Action? Activated;
    public event Action? NativeNavigationRequested;

    public ExplorerHost(string initialPath) : this(initialPath, ShellPathResolver.ResolveAsync, TimeSpan.FromSeconds(15)) { }

    internal ExplorerHost(string initialPath, Func<string, CancellationToken, Task<byte[]>> resolvePath, TimeSpan preparationTimeout)
    {
        this.resolvePath = resolvePath; this.preparationTimeout = preparationTimeout;
        pendingPath = initialPath;
        Focusable = true;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        child = Native.CreateWindowEx(0, "static", "", 0x56000000, 0, 0, 1, 1, hwndParent.Handle, 0, 0, 0);
        if (child == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            DiagnosticLog.Write("Creating ExplorerBrowser");
            browser = (IExplorerBrowser)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("71F96385-DDD6-48D3-A0C1-AE06E8B055FB"), true)!)!;
            DiagnosticLog.Write("Setting site");
            site = new BrowserSite(this);
            ((IObjectWithSite)browser).SetSite(site);
            var rect = new NativeRect { Right = 1, Bottom = 1 };
            var settings = new FolderSettings { ViewMode = 4 }; // 詳細表示
            browser.Initialize(child, ref rect, ref settings);
            DiagnosticLog.Write("ExplorerBrowser initialized");
            initialized = true;
            browser.SetOptions(0x08 | 0x40 | 0x80); // 履歴はアプリ管理、枠なし、表示設定を永続化しない
            browser.Advise(site, out cookie);
            advised = true;
            // WPFのレイアウト処理中はCOMのメッセージポンプを回せない。
            // BrowseToIDListはレイアウトが完了してから呼び出す。
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (browser != null) { IsNavigating = false; Navigate(pendingPath); }
                DiagnosticLog.Write("Initial navigation requested");
            });
        }
        catch (Exception ex)
        {
            ReleaseBrowser();
            ReportError($"シェルビューを初期化できません: {ex.Message}");
        }
        return new HandleRef(this, child);
    }

    public bool Navigate(string path)
    {
        if (browser == null) { pendingPath = path; return false; }
        if (!CanNavigate) return false;
        preparation?.Cancel();
        var version = ++requestVersion;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(path)) throw new IOException("フォルダーのパスを入力してください。");
            path = Path.GetFullPath(path, string.IsNullOrEmpty(CurrentPath) ? Environment.CurrentDirectory : CurrentPath);
            var cancellation = new CancellationTokenSource(preparationTimeout);
            preparation = cancellation;
            IsNavigating = true;
            NavigationStateChanged?.Invoke();
            _ = PrepareAndNavigateAsync(path, version, cancellation);
            return true;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ReportError($"移動できません: {ex.Message}");
            return false;
        }
    }

    private async Task PrepareAndNavigateAsync(string path, long version, CancellationTokenSource cancellation)
    {
        nint pidl = 0;
        try
        {
            var bytes = await resolvePath(path, cancellation.Token).WaitAsync(cancellation.Token);
            if (version != requestVersion || browser == null) return;
            pidl = Marshal.AllocCoTaskMem(bytes.Length); Marshal.Copy(bytes, 0, pidl, bytes.Length);
            submitting = true;
            shellNavigating = true;
            // ビューを所有するUIのSTA上でだけBrowseToIDListを呼ぶ。
            browser.BrowseToIDList(pidl, 0);
        }
        catch (OperationCanceledException)
        {
            if (version == requestVersion && browser != null) ReportError("移動できません: 移動先の確認がタイムアウトしました。WSLの起動状態やパスを確認して再試行してください。");
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (version == requestVersion && browser != null) ReportError($"移動できません: {ex.Message}");
        }
        finally
        {
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (ReferenceEquals(preparation, cancellation)) preparation = null;
            cancellation.Dispose();
        }
    }

    internal void NavigationComplete(nint pidl)
    {
        DiagnosticLog.Write("Navigation complete");
        // PIDLはコールバック中だけ有効。ここで文字列に変換する。
        var hr = Native.SHGetNameFromIDList(pidl, 0x80058000, out var name); // FILESYSPATH
        if (hr < 0) hr = Native.SHGetNameFromIDList(pidl, 0x80028000, out name); // DESKTOPABSOLUTEPARSING
        if (hr < 0) { ReportError("現在の場所の名前を取得できません。"); return; }
        try
        {
            CurrentPath = Marshal.PtrToStringUni(name) ?? "";
            var path = CurrentPath;
            var version = requestVersion;
            Dispatcher.BeginInvoke(() =>
            {
                if (version != requestVersion || browser == null) return;
                shellNavigating = false;
                submitting = false;
                IsNavigating = false;
                Navigated?.Invoke(path);
                NavigationStateChanged?.Invoke();
            });
        }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    internal void ReportError(string message)
    {
        DiagnosticLog.Write(message);
        var version = requestVersion;
        Dispatcher.BeginInvoke(() => { if (version != requestVersion) return; submitting = false; shellNavigating = false; IsNavigating = false; Error?.Invoke(message); NavigationStateChanged?.Invoke(); });
    }
    internal int NavigationPending()
    {
        if (shellNavigating && !submitting) return unchecked((int)0x80004004); // シェルに渡した移動は直列化する。
        if (!submitting) { preparation?.Cancel(); ++requestVersion; NativeNavigationRequested?.Invoke(); }
        submitting = false;
        shellNavigating = true;
        IsNavigating = true;
        Dispatcher.BeginInvoke(() => NavigationStateChanged?.Invoke());
        return 0;
    }
    internal void NotifyActivated() => Dispatcher.BeginInvoke(() => Activated?.Invoke());
    public bool ContainsNativeFocus => child != 0 && (Native.GetFocus() == child || Native.IsChild(child, Native.GetFocus()));

    private IShellView? GetView()
    {
        if (browser == null) return null;
        try
        {
            var iid = typeof(IShellView).GUID;
            browser.GetCurrentView(ref iid, out var view);
            return view;
        }
        catch (COMException) { return null; }
    }

    public bool FocusView()
    {
        var view = GetView();
        if (view == null) return false;
        try
        {
            view.UIActivate(2); // SVUIA_ACTIVATE_FOCUS
            view.GetWindow(out var hwnd);
            if (!ContainsNativeFocus) Native.SetFocus(hwnd);
            NotifyActivated();
            return true;
        }
        catch (COMException ex) { ReportError(ex.Message); return false; }
        finally { Marshal.ReleaseComObject(view); }
    }

    // 現在の選択から取得する。表示名の連結やクリップボードは使用しない。
    public string? GetSingleSelectedFile()
    {
        if (IsNavigating) return null;
        var view = GetView();
        IShellItemArray? items = null; IShellItem? item = null; nint name = 0;
        try
        {
            if (view is not IFolderView folder) return null;
            folder.ItemCount(1, out var selected); // SVGIO_SELECTION
            if (selected != 1) return null;
            var iid = typeof(IShellItemArray).GUID;
            folder.Items(1, ref iid, out items);
            items.GetCount(out var count);
            if (count != 1) return null;
            items.GetItemAt(0, out item);
            item.GetAttributes(0x60400000, out var attributes); // FILESYSTEM | FOLDER | STREAM
            if ((attributes & 0x40000000) == 0) return null;
            // ZIP等はFOLDERとSTREAMの両方を持つが、実体はプレビュー対象のファイル。
            if ((attributes & 0x20400000) == 0x20000000) return null;
            item.GetDisplayName(0x80058000, out name); // SIGDN_FILESYSPATH
            return Marshal.PtrToStringUni(name);
        }
        catch (COMException ex) { DiagnosticLog.Write($"QuickLook selection unavailable: {ex.Message}"); return null; }
        finally
        {
            if (name != 0) Marshal.FreeCoTaskMem(name);
            if (item != null) Marshal.ReleaseComObject(item);
            if (items != null) Marshal.ReleaseComObject(items);
            if (view != null) Marshal.ReleaseComObject(view);
        }
    }

    public bool TranslateShellKey(ref MSG msg)
    {
        // ExplorerBrowser全体に転送し、名前変更等の内部編集状態も尊重する。
        return browser is IInputObject input && input.TranslateAcceleratorIO(ref msg) == 0;
    }

    protected override bool TabIntoCore(TraversalRequest request) => FocusView();
    protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers) => false; // MainWindowで一度だけ転送する

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0005 && browser != null) // WM_SIZE、物理ピクセルを使う
        {
            Native.GetClientRect(hwnd, out var rect);
            browser.SetRect(0, rect);
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ReleaseBrowser();
        Native.DestroyWindow(hwnd.Handle);
        child = 0;
    }

    private void ReleaseBrowser()
    {
        ++requestVersion; preparation?.Cancel();
        if (browser == null) return;
        DiagnosticLog.Write("Destroying ExplorerBrowser");
        // 1つの解除が失敗しても残りのネイティブ資源を解放する。
        try { if (advised) browser.Unadvise(cookie); } catch (COMException ex) { DiagnosticLog.Write($"Unadvise failed: {ex.Message}"); }
        try { if (initialized) browser.Destroy(); } catch (COMException ex) { DiagnosticLog.Write($"Destroy failed: {ex.Message}"); }
        try { ((IObjectWithSite)browser).SetSite(null); } catch (COMException ex) { DiagnosticLog.Write($"SetSite(null) failed: {ex.Message}"); }
        Marshal.ReleaseComObject(browser);
        browser = null;
        site = null;
        advised = initialized = false;
        DiagnosticLog.Write("ExplorerBrowser released");
    }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class BrowserSite(ExplorerHost owner) : IExplorerBrowserEvents, IShellServiceProvider, ICommDlgBrowser
{
    public int OnNavigationPending(nint pidl) => owner.NavigationPending();
    public int OnViewCreated(nint view) => 0;
    public int OnNavigationComplete(nint pidl)
    {
        try { owner.NavigationComplete(pidl); return 0; }
        catch (Exception ex) { owner.ReportError(ex.Message); return ex.HResult; }
    }
    public int OnNavigationFailed(nint pidl) { owner.ReportError("フォルダーへの移動に失敗しました。"); return 0; }
    public int QueryService(ref Guid service, ref Guid iid, out nint result)
    {
        result = 0;
        if (service != typeof(ICommDlgBrowser).GUID || iid != typeof(ICommDlgBrowser).GUID)
            return unchecked((int)0x80004002);
        result = Marshal.GetComInterfaceForObject(this, typeof(ICommDlgBrowser));
        return 0;
    }
    public int OnDefaultCommand(nint view) => 1; // 標準の開く動作に任せる
    public int OnStateChange(nint view, uint change) { if (change == 0) owner.NotifyActivated(); return 0; } // CDBOSC_SETFOCUS
    public int IncludeObject(nint view, nint pidl) => 0;
}
