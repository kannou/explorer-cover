using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace ExplorerCover.Shell;

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)]
public struct FolderSettings { public uint ViewMode, Flags; }

// メソッド順・引数はWindows SDK ShObjIdl_core.hのCOM ABIと一致させる。
[ComImport, Guid("dfd3b6b5-c10c-4be9-85f6-a66969f402f6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowser
{
    void Initialize(nint parent, ref NativeRect rect, ref FolderSettings settings);
    void Destroy();
    void SetRect(nint deferWindowPos, NativeRect rect);
    void SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string name);
    void SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string text);
    void SetFolderSettings(ref FolderSettings settings);
    void Advise(IExplorerBrowserEvents sink, out uint cookie);
    void Unadvise(uint cookie);
    void SetOptions(uint options);
    void GetOptions(out uint options);
    void BrowseToIDList(nint pidl, uint flags);
    void BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object item, uint flags);
    void FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object item, uint flags);
    void RemoveAll();
    void GetCurrentView(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellView view);
}

[ComVisible(true), Guid("361bbdc7-e6ee-4e13-be58-58e2240c810f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowserEvents
{
    [PreserveSig] int OnNavigationPending(nint pidl);
    [PreserveSig] int OnViewCreated(nint view);
    [PreserveSig] int OnNavigationComplete(nint pidl);
    [PreserveSig] int OnNavigationFailed(nint pidl);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellView
{
    void GetWindow(out nint hwnd);
    void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
    [PreserveSig] int TranslateAccelerator(ref MSG msg);
    void EnableModeless([MarshalAs(UnmanagedType.Bool)] bool enable);
    void UIActivate(uint state);
    void Refresh();
    void CreateViewWindow(nint previous, nint settings, nint shellBrowser, nint rect, out nint hwnd);
    void DestroyViewWindow();
    void GetCurrentInfo(nint settings);
    void AddPropertySheetPages(uint reserved, nint callback, nint parameter);
    void SaveViewState();
    void SelectItem(nint pidl, uint flags);
    void GetItemObject(uint item, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IContextMenu menu);
}

[ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
    void InvokeCommand(ref InvokeCommandInfo info);
}
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct InvokeCommandInfo
{
    public int Size; public uint Mask; public nint Window;
    [MarshalAs(UnmanagedType.LPStr)] public string Verb;
    public nint Parameters, Directory; public int Show; public uint HotKey; public nint Icon;
}

[ComImport, Guid("68284FAA-6A48-11D0-8C78-00C04FD918B4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInputObject
{
    [PreserveSig] int UIActivateIO([MarshalAs(UnmanagedType.Bool)] bool activate, ref MSG msg);
    [PreserveSig] int HasFocusIO();
    [PreserveSig] int TranslateAcceleratorIO(ref MSG msg);
}

[ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IObjectWithSite
{
    void SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site);
    void GetSite(ref Guid iid, out nint site);
}

[ComVisible(true), Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out nint result);
}

[ComVisible(true), Guid("000214F1-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICommDlgBrowser
{
    [PreserveSig] int OnDefaultCommand(nint view);
    [PreserveSig] int OnStateChange(nint view, uint change);
    [PreserveSig] int IncludeObject(nint view, nint pidl);
}

[ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView
{
    void GetCurrentViewMode(out uint mode);
    void SetCurrentViewMode(uint mode);
    void GetFolder(ref Guid iid, out nint folder);
    void Item(int index, out nint pidl);
    void ItemCount(uint flags, out int count);
    void Items(uint flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray items);
    void GetSelectionMarkedItem(out int index);
    void GetFocusedItem(out int index);
}

[ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    void BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
    void GetPropertyStore(int flags, ref Guid iid, out nint result);
    void GetPropertyDescriptionList(nint key, ref Guid iid, out nint result);
    void GetAttributes(uint flags, uint mask, out uint attributes);
    void GetCount(out uint count);
    void GetItemAt(uint index, out IShellItem item);
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint type, out nint name);
    void GetAttributes(uint mask, out uint attributes);
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("imm32.dll")] internal static extern nint ImmGetContext(nint hwnd);
    [DllImport("imm32.dll")] internal static extern bool ImmReleaseContext(nint hwnd, nint context);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)] internal static extern int ImmGetCompositionStringW(nint context, uint index, nint buffer, uint length);
    internal static bool IsComposingText()
    {
        var hwnd = GetFocus(); var context = ImmGetContext(hwnd);
        if (context == 0) return false;
        try { return ImmGetCompositionStringW(context, 8, 0, 0) > 0; }
        finally { ImmReleaseContext(hwnd, context); }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] internal static extern nint GetFocus();
    [DllImport("user32.dll")] internal static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    internal static bool IsEditingText()
    {
        var name = new StringBuilder(256);
        GetClassName(GetFocus(), name, name.Capacity);
        return name.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase) || name.ToString().StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll")]
    internal static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
}
