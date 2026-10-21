using System.Windows;
using ToolSuite.Core;

namespace ToolSuite.App;

/// <summary>统一设置的按需创建与关闭释放；宿主与整合检查共用同一入口。</summary>
internal sealed class SettingsHost(SuiteController controller)
{
    private SettingsWindow? window;
    public SettingsWindow? Current => window;
    public SettingsWindow Show()
    {
        if (window is null)
        {
            window = new SettingsWindow(controller);
            window.Closed += (_, _) => window = null;
            window.Show();
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        return window;
    }
    public void Close() => window?.Close();
}
