using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ExplorerCover.Core;

namespace ExplorerCover;

internal static class WindowLayout
{
    public static WindowSnapshot? Capture(Window window, bool maximized)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (hwnd == 0 || !GetWindowPlacement(hwnd, ref placement)) return null;
        if (!TryInfo(MonitorFromWindow(hwnd, 2), out var monitor)) return null;
        var rect = placement.Normal;
        // WINDOWPLACEMENTの通常矩形は作業領域座標。保存時は画面座標に統一する。
        return new(rect.Left + monitor.Work.Left - monitor.Bounds.Left, rect.Top + monitor.Work.Top - monitor.Bounds.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top, maximized);
    }
    public static void RestoreOnShow(Window window, WindowSnapshot? saved)
    {
        if (saved == null) return; // 追加前のJSONは既定のサイズ・位置で起動する。
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = saved.Maximized ? WindowState.Maximized : WindowState.Normal;
        window.SourceInitialized += (_, _) =>
        {
            var rect = new Rect { Left = saved.Left, Top = saved.Top, Right = saved.Left + saved.Width, Bottom = saved.Top + saved.Height };
            if (!TryInfo(MonitorFromRect(ref rect, 2), out var monitor)) { DiagnosticLog.Write("Monitor work area could not be read."); return; }
            var fitted = saved.FitToWorkArea(monitor.Work.Left, monitor.Work.Top, monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
            var offsetX = monitor.Work.Left - monitor.Bounds.Left; var offsetY = monitor.Work.Top - monitor.Bounds.Top;
            var placement = new Placement
            {
                Length = Marshal.SizeOf<Placement>(), Show = saved.Maximized ? 3 : 1,
                Normal = new Rect { Left = fitted.Left - offsetX, Top = fitted.Top - offsetY, Right = fitted.Left + fitted.Width - offsetX, Bottom = fitted.Top + fitted.Height - offsetY }
            };
            if (!SetWindowPlacement(new WindowInteropHelper(window).Handle, ref placement)) DiagnosticLog.Write("Window placement could not be restored.");
        };
    }
    private static bool TryInfo(nint monitor, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Placement { public int Length, Flags, Show; public Point Minimum, Maximum; public Rect Normal; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Bounds, Work; public int Flags; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
