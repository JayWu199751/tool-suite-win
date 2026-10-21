using ClipboardTool.Domain.Hotkeys;
namespace ClipboardTool.Infrastructure.Windows;
public static class HotkeyAvailability
{
    public static bool CanRegister(HotkeyCombo combo)
    {
        const int id = 0x6A77;
        if (!NativeMethods.RegisterHotKey(IntPtr.Zero,id,(uint)combo.Modifiers,combo.VirtualKey)) return false;
        NativeMethods.UnregisterHotKey(IntPtr.Zero,id);
        return true;
    }
}
