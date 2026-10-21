using System.IO;
using PerfMonitor.App.Shell;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;
using PerfMonitor.Windows.Settings;
using ToolSuite.Core;

namespace PerfMonitor.App;

/// <summary>唯一宿主管理的性能条入口；构造与 Configure 不创建窗口或采样源。</summary>
public sealed class PerfMonitorModule : IToolModule
{
    private ModuleContext? _context;
    private ThemePreference _theme = ThemePreference.Light;
    private WpfStartupShellHost? _host;
    private StartupShellController? _controller;
    private SettingsWindow? _offlineSettings;
    public string Id => "perf-monitor";
    public string DisplayName => "性能条";
    public IReadOnlyList<ToolCommand> Commands =>
    [
        new("visibility", _controller?.State.IsPerformanceBarVisible == true ? "隐藏性能条" : "显示性能条", _ => Run(() =>
            _controller?.SelectMenuItem(ShellMenuAction.TogglePerformanceBarVisibility))),
        new("settings", "性能条设置", _ => Run(OpenSettings), RequiresRunning: false)
    ];

    public void Configure(ModuleContext context)
    {
        _context = context;
        _theme = context.Theme;
    }
    public Task StartAsync(ModuleContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Configure(context);
        if (_controller is not null) return Task.CompletedTask;
        CloseOfflineSettings();
        _host = new WpfStartupShellHost(Store(), context.ReportError);
        try
        {
            _controller = new StartupShellController(_host);
            _controller.ApplyTheme(EffectiveTheme);
            _host.ContextMenuRequested += OnContextMenu;
            _host.HideRequested += OnHide;
            _controller.Start();
        }
        catch
        {
            // 宿主随后调用 StopAsync，保留引用以清理任何部分启动的计时器和查询。
            throw;
        }
        return Task.CompletedTask;
    }
    public Task PrepareToStopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _controller?.PrepareSave();
        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var controller = _controller;
        var host = _host;
        _controller = null;
        _host = null;
        try { if (controller is not null) await controller.StopAsync(); }
        finally
        {
            if (host is not null)
            {
                host.ContextMenuRequested -= OnContextMenu;
                host.HideRequested -= OnHide;
                host.Dispose();
            }
            CloseOfflineSettings();
        }
    }
    public void ApplyTheme(ThemePreference theme)
    {
        _theme = theme;
        _controller?.ApplyTheme(EffectiveTheme);
        if (_offlineSettings is not null)
            _offlineSettings.ApplySettings(Store().Load() with { Theme = EffectiveTheme, Autostart = false }, false);
    }
    private BarTheme EffectiveTheme => _theme == ThemePreference.Dark ? BarTheme.Dark : BarTheme.Light;
    private WindowsSettingsStore Store() => new(Path.Combine(
        (_context ?? throw new InvalidOperationException("模块尚未配置数据位置。")).DataDirectory, "settings.json"));
    private void OnContextMenu(object? sender, EventArgs e) => _controller?.OnPerformanceBarRightClick();
    private void OnHide(object? sender, EventArgs e)
    {
        try
        {
            if (_controller?.State.IsPerformanceBarVisible == true)
                _controller.SelectMenuItem(ShellMenuAction.TogglePerformanceBarVisibility);
        }
        catch (Exception exception) { _context?.ReportError(exception.Message); }
    }
    private void OpenSettings()
    {
        if (_controller is { } controller)
        {
            // 直接使用同一公开设置更新入口；关闭窗口立即释放，不进入旧 App 的隐藏链。
            _host!.ShowSettingsWindow(controller.Settings, controller.UpdateSettings, controller.RecoveredInvalidSettingsOnStartup);
            return;
        }
        var store = Store();
        if (_offlineSettings is null)
        {
            _offlineSettings = new SettingsWindow(store.Load() with { Theme = EffectiveTheme, Autostart = false }, patch =>
            {
                var next = store.Load().Apply(patch) with { Theme = EffectiveTheme, Autostart = false };
                store.Save(next);
                return next;
            }, store.RecoveredInvalidSettingsOnLastLoad, listenForSystemTheme: false);
            _offlineSettings.Closed += OfflineClosed;
        }
        _offlineSettings.Show();
        _offlineSettings.Activate();
    }
    private void OfflineClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window) window.Closed -= OfflineClosed;
        _offlineSettings = null;
    }
    private void CloseOfflineSettings()
    {
        if (_offlineSettings is not { } window) return;
        _offlineSettings = null;
        window.Closed -= OfflineClosed;
        window.Close();
    }
    private static Task Run(Action action) { action(); return Task.CompletedTask; }
}
