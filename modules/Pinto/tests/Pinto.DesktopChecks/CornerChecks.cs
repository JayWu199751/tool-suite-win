using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pinto.Views;

namespace Pinto.DesktopChecks;

internal static class CornerChecks
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    public static bool HasNativeDialogAppearance(ImportDialog dialog)
    {
        var handle = new WindowInteropHelper(dialog).Handle;
        var nativeFrame = (GetWindowLong(handle, -16) & 0x00C00000) == 0x00C00000;
        var result = DwmGetWindowAttribute(handle, 20, out var dark, sizeof(int));
        var expected = Pinto.Services.ThemeService.IsDark && !System.Windows.SystemParameters.HighContrast ? 1 : 0;
        return nativeFrame && !dialog.AllowsTransparency && result == 0 && dark == expected;
    }

    public static bool HasSingleVisualSurface(NoteWindow window, out string detail)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var layered = (GetWindowLong(handle, -20) & 0x00080000) != 0;
        var nativeFrame = (GetWindowLong(handle, -16) & 0x00C00000) != 0;
        var backdropResult = DwmGetWindowAttribute(handle, 38, out var backdrop, sizeof(int));
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Round(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Round(window.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var x = (int)Math.Round(3 * dpi.DpiScaleX);
        var y = (int)Math.Round(3 * dpi.DpiScaleY);
        var cornersTransparent = Alpha(x, y) == 0 && Alpha(width - x - 1, y) == 0
            && Alpha(x, height - y - 1) == 0 && Alpha(width - x - 1, height - y - 1) == 0;
        var centerAlpha = Alpha(width / 2, height / 2);
        detail = $"逐像素透明={layered}，系统背景={backdrop}，原生框架={nativeFrame}，四角透明={cornersTransparent}，内容Alpha={centerAlpha}";
        // 单独的区域裁剪不足以约束 DWM 背景；必须同时验证分层窗口、背景禁用和实际内容 alpha。
        return window.AllowsTransparency && layered && !nativeFrame && cornersTransparent && centerAlpha > 0
            && (Environment.OSVersion.Version.Build < 22621 || (backdropResult == 0 && backdrop == 1));

        byte Alpha(int px, int py) => pixels[(py * width + px) * 4 + 3];
    }
}
