using System.Windows;
using ToolSuite.Core;

namespace ToolSuite.App;

/// <summary>统一设置的按需创建与关闭释放；宿主与整合检查共用同一入口。</summary>
internal sealed class SettingsHost : IDisposable
{
    private readonly SuiteController controller;
    private UnifiedSettingsPage? window;
    public SettingsHost(SuiteController controller)
    {
        this.controller = controller;
        controller.SettingsRequested += ShowSection;
    }
    private void ShowSection(string section) => Show(section);
    public UnifiedSettingsPage? Current => window;
    public UnifiedSettingsPage Show(string? section = null)
    {
        if (window is null)
        {
            window = new UnifiedSettingsPage(controller);
            window.View.Closed += (_, _) => window = null;
            window.View.Show();
        }
        if (window.View.WindowState == WindowState.Minimized) window.View.WindowState = WindowState.Normal;
        window.View.Activate();
        if (section is not null) window.OpenSection(section);
        return window;
    }
    public void Close() => window?.View.Close();
    public void Dispose()
    {
        controller.SettingsRequested -= ShowSection;
        Close();
    }
}
