using System.Reflection;
using System.Runtime.InteropServices;
using ToolSuite.Installer;

namespace ToolSuite.InstallerUiChecks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var output = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
            if (output is not null) Directory.CreateDirectory(output);
            Check(false, output);
            Check(true, output);
            var highContrast = new Palette(true, highContrast: true);
            if (highContrast.Canvas != SystemColors.Window || highContrast.Ink != SystemColors.WindowText)
                throw new Exception("高对比度未优先使用系统颜色");
            using var systemForm = new InstallerForm(InstallerOptions.Parse([]));
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            AssertTheme(systemForm, !SystemInformation.HighContrast && key?.GetValue("AppsUseLightTheme") is int value && value == 0, "实际系统偏好");
            Console.WriteLine("PASS 安装窗口浅色/深色启动、运行中主题切换与输入框颜色同步");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL " + exception.Message);
            return 1;
        }
    }

    private static void Check(bool initialDark, string? output)
    {
        var dark = initialDark;
        var options = InstallerOptions.Parse([]);
        using var form = new InstallerForm(options, () => dark);
        _ = form.Handle;
        AssertTheme(form, dark, "启动", output);
        var input = form.Controls.OfType<TextBox>().Single();
        input.Text = @"C:\custom-install-directory";
        foreach (var message in new uint[] { 0x001A, 0x031A, 0x0015 })
        {
            dark = !dark;
            SendMessage(form.Handle, message, IntPtr.Zero, IntPtr.Zero);
            AssertTheme(form, dark, $"系统通知 0x{message:X4}");
            if (input.Text != @"C:\custom-install-directory")
                throw new Exception("切换主题改变了安装目录");
        }
    }

    private static void AssertTheme(InstallerForm form, bool dark, string stage, string? output = null)
    {
        _ = form.Handle;
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        // 直接调用真实 OnPaint，避免原生标题栏偏移干扰客户区像素取样。
        using (var graphics = Graphics.FromImage(bitmap))
        using (var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, form.ClientSize)))
            typeof(InstallerForm).GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [args]);
        var canvas = bitmap.GetPixel(4, 4);
        if ((canvas.GetBrightness() < 0.5f) != dark)
            throw new Exception($"{stage}预期{(dark ? "深色" : "浅色")}画布，实际 RGB({canvas.R},{canvas.G},{canvas.B})");
        var input = form.Controls.OfType<TextBox>().Single();
        if ((input.BackColor.GetBrightness() < 0.5f) != dark
            || (input.ForeColor.GetBrightness() > 0.5f) != dark)
            throw new Exception($"{stage}安装目录输入框未同步主题");
        var result = DwmGetWindowAttribute(form.Handle, 20, out var titleDark, sizeof(int));
        if (result == 0 && (titleDark != 0) != dark)
            throw new Exception($"{stage}标题栏未同步主题");
        if (output is not null)
        {
            input.DrawToBitmap(bitmap, input.Bounds);
            bitmap.Save(Path.Combine(output, dark ? "installer-dark.png" : "installer-light.png"));
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
