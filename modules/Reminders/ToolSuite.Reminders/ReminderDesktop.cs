using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace ToolSuite.Reminders;

internal static class ReminderDesktop
{
    public static void SetDarkTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = dark && !SystemParameters.HighContrast ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    // 直接使用 Win32，以免未来 .NET 修改 Environment.TickCount64 的睡眠语义。
    public static long ElapsedMilliseconds() => checked((long)GetTickCount64());

    public static bool CanShowReminder()
    {
        var desktop = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
        if (desktop == IntPtr.Zero) return false;
        try
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _)
                && name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(desktop); }
    }

    public static void Position(Window window, Forms.Screen screen)
    {
        var area = screen.WorkingArea;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var point = new NativePoint(area.Left + 1, area.Top + 1);
        var monitor = MonitorFromPoint(point, 2);
        var result = GetDpiForMonitor(monitor, 0, out var dpiX, out _);
        var scale = result == 0 ? dpiX / 96d : 1;
        window.MaxHeight = Math.Max(100, (area.Height - 32) / scale);
        window.Width = Math.Max(100, Math.Min(380 * ReminderUi.TextScale, (area.Width - 32) / scale));
        var width = (int)Math.Ceiling(window.Width * scale);
        var height = (int)Math.Ceiling(window.ActualHeight * scale);
        SetWindowPos(handle, new IntPtr(-1), Math.Max(area.Left, area.Right - width - 16),
            Math.Max(area.Top, area.Bottom - height - 16), 0, 0, 0x0010 | 0x0001); // NOACTIVATE | NOSIZE
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y) { public readonly int X = x; public readonly int Y = y; }
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder name, int length, out int needed);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
