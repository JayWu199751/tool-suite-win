using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Pinto.Services;

internal static class NativeWindow
{
    // 原生窗口边界以物理像素计；间距不随 DPI 放大。
    private const int DockingGapPixels = 2;
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    public static void EnsureTrayOnly(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var extended = GetWindowLong(handle, -20);
        // 保留分层透明等现有样式，工具窗口不进入任务栏。
        var trayOnly = (extended | 0x00000080) & ~0x00040000;
        if (extended == trayOnly) return;
        SetWindowLong(handle, -20, trayOnly);
        SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
    }

    public static bool ApplyBackdrop(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return false;
        var style = GetWindowLong(handle, -16);
        SetWindowLong(handle, -16, style & ~0x00010000 & ~0x00020000); // 去除原生最大化、最小化能力。
        var dark = ThemeService.IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(handle, 34, ref noBorder, sizeof(int));

        if (window.AllowsTransparency)
        {
            // 分层窗口仅由 WPF 的 alpha 表面绘制；DWM 背景不会跟随内容的透明圆角。
            var noRounding = 1;
            DwmSetWindowAttribute(handle, 33, ref noRounding, sizeof(int));
            var noBackdrop = 1;
            DwmSetWindowAttribute(handle, 38, ref noBackdrop, sizeof(int));
            var noGlass = new Margins();
            DwmExtendFrameIntoClientArea(handle, ref noGlass);
            return false;
        }

        var rounded = 2;
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));

        if (Environment.OSVersion.Version.Build < 22621) return false;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref margins);
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        var acrylic = 3;
        return DwmSetWindowAttribute(handle, 38, ref acrylic, sizeof(int)) == 0;
    }

    public static void ApplyDialogAppearance(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        // 标准 Windows 标题栏仅同步明暗外观，不添加 Acrylic 或自绘窗口按钮。
        var dark = ThemeService.IsDark && !SystemParameters.HighContrast ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    public static DockEdges? SnapMovingBounds(Window window, nint boundsPointer, bool resizing, IEnumerable<Window>? peers = null)
    {
        if (boundsPointer == 0) return null;
        var rect = Marshal.PtrToStructure<Rect>(boundsPointer);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref rect, 2), ref info)) return null;
        var handle = new WindowInteropHelper(window).Handle;
        var dpi = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        var bounds = new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var work = new WindowBounds(info.Work.Left + DockingGapPixels, info.Work.Top + DockingGapPixels,
            info.Work.Right - info.Work.Left - 2 * DockingGapPixels, info.Work.Bottom - info.Work.Top - 2 * DockingGapPixels);
        WindowBounds? previousBounds = !resizing && GetWindowRect(handle, out var previousRect)
            ? new(previousRect.Left, previousRect.Top, previousRect.Right - previousRect.Left, previousRect.Bottom - previousRect.Top) : null;
        var result = EdgeDocking.Snap(bounds, work, 12 * dpi, resizing, previousBounds);
        if (!resizing && peers is not null)
        {
            var otherBounds = new List<WindowBounds>();
            foreach (var peer in peers)
            {
                if (ReferenceEquals(peer, window) || !peer.IsVisible || peer.WindowState == WindowState.Minimized) continue;
                var peerHandle = new WindowInteropHelper(peer).Handle;
                if (peerHandle != 0 && GetWindowRect(peerHandle, out var peerRect))
                    otherBounds.Add(new(peerRect.Left, peerRect.Top, peerRect.Right - peerRect.Left, peerRect.Bottom - peerRect.Top));
            }
            var snapped = NoteDocking.Snap(result.Bounds, work, otherBounds, 12 * dpi, result.Edges, previousBounds, gap: DockingGapPixels);
            // 屏幕贴边记忆只记录真实接触屏幕的边，不能把便签间贴边恢复到屏幕上。
            result = new(snapped, EdgeDocking.Snap(snapped, work, 0).Edges);
        }
        if (resizing && (result.Bounds.Width < window.MinWidth * dpi || result.Bounds.Height < window.MinHeight * dpi))
            return EdgeDocking.Snap(bounds, work, 0).Edges;
        rect.Left = (int)Math.Round(result.Bounds.Left);
        rect.Top = (int)Math.Round(result.Bounds.Top);
        rect.Right = (int)Math.Round(result.Bounds.Right);
        rect.Bottom = (int)Math.Round(result.Bounds.Bottom);
        Marshal.StructureToPtr(rect, boundsPointer, false);
        return result.Edges;
    }

    public static void EnsureOnScreen(Window window, DockEdges edges = DockEdges.None)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || !GetWindowRect(handle, out var rect)) return;
        var monitor = MonitorFromRect(ref rect, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var work = info.Work;
        work.Left += DockingGapPixels;
        work.Top += DockingGapPixels;
        work.Right -= DockingGapPixels;
        work.Bottom -= DockingGapPixels;
        var dpi = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        var width = Math.Min(rect.Right - rect.Left, work.Right - work.Left);
        var height = Math.Min(rect.Bottom - rect.Top, work.Bottom - work.Top);
        // 位置来自旧显示器时，以当前显示器的物理工作区重新约束，避免窗口永久丢失。
        if (width < window.MinWidth * dpi) width = (int)Math.Min(window.MinWidth * dpi, work.Right - work.Left);
        if (height < window.MinHeight * dpi) height = (int)Math.Min(window.MinHeight * dpi, work.Bottom - work.Top);
        var x = Math.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - width));
        var y = Math.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
        var aligned = EdgeDocking.Align(new(x, y, width, height), new(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), edges);
        x = (int)Math.Round(aligned.Left);
        y = (int)Math.Round(aligned.Top);
        SetWindowPos(handle, 0, x, y, width, height, 0x0004 | 0x0010); // 不改变 Z 序、不激活。
    }
}
