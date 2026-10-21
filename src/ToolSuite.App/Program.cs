using System.Diagnostics;
using System.IO;
using System.Windows;
using ToolSuite.Core;
using Application = System.Windows.Application;

namespace ToolSuite.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // 必须早于 Application/任何便签窗口，避免旧触笔线程阻塞窗口销毁。
            AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
            var dataIndex = Array.IndexOf(args, "--data-root");
            var root = dataIndex >= 0 ? Path.GetFullPath(args[dataIndex + 1])
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolSuite");
            using var instance = new SingleInstance(root);
            if (!instance.IsPrimary) { instance.NotifyAsync().GetAwaiter().GetResult(); return 0; }
            var application = new SuiteApplication(root, instance, args.Contains("--verify-host"));
            application.Run();
            return application.ExitCode;
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "工具套件启动失败"); return 1; }
    }
}

internal sealed class SuiteApplication : Application
{
    private readonly string root;
    private readonly SingleInstance instance;
    private readonly bool verify;
    private SuiteController? controller;
    private SuiteTheme? theme;
    private TrayHost? tray;
    private SettingsHost? settings;
    private SuiteShutdown? shutdown;
    private bool exiting;
    public int ExitCode { get; private set; }
    public SuiteApplication(string root, SingleInstance instance, bool verify)
    { this.root = root; this.instance = instance; this.verify = verify; ShutdownMode = ShutdownMode.OnExplicitShutdown; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            // 此模式只检查宿主基础设施；真实三个模块由各模块及整套检查入口验证。
            controller = new SuiteController(new SuiteSettingsStore(root), verify ? [] : ModuleCatalog.CreateModules(), CreateAutoStart());
            tray = new TrayHost(controller, ShowSettings, RequestExitAsync);
            theme = new SuiteTheme(controller, Dispatcher);
            // 托盘配色跟随同一有效外观，暗色/高对比度下保持可读。
            theme.AppearanceApplied += tray.ApplyAppearance;
            tray.ApplyAppearance(theme.IsDark, theme.IsHighContrast);
            settings = new SettingsHost(controller);
            shutdown = new SuiteShutdown(controller, () => { settings?.Close(); Shutdown(); }, tray.ShowError);
            controller.ErrorReported += tray.ShowError;
            instance.Listen(() => Dispatcher.BeginInvoke(ShowSettings));
            await controller.StartAsync();
            if (verify) await VerifyAsync();
        }
        catch (Exception ex)
        {
            ExitCode = 1;
            if (verify) { Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "verification-failed.txt"), ex.ToString()); }
            else System.Windows.MessageBox.Show(ex.Message, "工具套件启动失败");
            Shutdown(1);
        }
    }
    private void ShowSettings()
    {
        if (exiting || settings is null) return;
        settings.Show();
    }
    /// <summary>只有从安装后的正式 EXE 运行才注册开机启动；dotnet 宿主路径不能写进 Run 值。</summary>
    private static ISuiteAutoStart? CreateAutoStart()
    {
        var executable = Environment.ProcessPath;
        if (executable is null || Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return null;
        return new SuiteAutoStart(new HkcuRunValueStore(), executable);
    }
    private async Task RequestExitAsync()
    {
        if (exiting || shutdown is null) return;
        exiting = true;
        try
        {
            if (!await shutdown.TryExitAsync()) exiting = false;
        }
        catch (Exception ex) { exiting = false; tray?.ShowError(ex.Message); }
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (shutdown is null) return;
        // 已有退出流程在进行：先阻止系统跳过它，由该流程完成保存后自行退出。
        if (exiting) { e.Cancel = true; return; }
        exiting = true;
        // 系统注销/关机不能绕过模块编辑提交与保存失败保护：在 UI 线程用嵌套消息帧等待
        // 异步保存预检（模块入口要求 UI 线程，直接同步等待会死锁），失败则阻止系统结束。
        var task = shutdown.PrepareAndStopAsync();
        try
        {
            if (!task.IsCompleted)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.FromCurrentSynchronizationContext());
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            if (!task.GetAwaiter().GetResult()) { exiting = false; e.Cancel = true; }
        }
        catch (Exception ex)
        {
            exiting = false;
            e.Cancel = true;
            tray?.ShowError(ex.Message);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        settings?.Dispose(); theme?.Dispose(); tray?.Dispose();
        base.OnExit(e);
    }
    private async Task VerifyAsync()
    {
        // 通过真实托盘菜单控件事件与真实设置窗口检查，不通过私有模块状态模拟用户结果。
        if (Windows.Count != 0) throw new Exception("空白启动显示了首页。");
        tray!.InvokeSettingsMenuForVerification();
        if (settings?.Current is not { IsVisible: true }) throw new Exception("托盘设置命令未显示窗口。");
        settings.Close();
        if (Windows.Count != 0) throw new Exception("设置关闭后窗口未释放。");
        var startup = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        // 验证可通过 dotnet ToolSuite.dll 启动，避免自动触发 UAC；正式 EXE 使用 requireAdministrator。
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startup.ArgumentList.Add(typeof(SuiteApplication).Assembly.Location);
        startup.ArgumentList.Add("--data-root"); startup.ArgumentList.Add(root);
        using var secondary = Process.Start(startup) ?? throw new Exception("无法启动重复实例。");
        await secondary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (secondary.ExitCode != 0) throw new Exception("重复实例未正常退出。");
        for (var i = 0; i < 30 && settings?.Current is null; i++) await Task.Delay(50);
        if (settings?.Current is not { IsVisible: true }) throw new Exception("重复启动没有通知原实例显示设置。");
        settings.Close();
        File.WriteAllText(Path.Combine(root, "verification-passed.txt"),
            "空白启动无首页；托盘设置命令；设置关闭释放；重复实例退出且原实例收到通知；安全退出。\n" + DateTimeOffset.Now);
        tray.InvokeExitMenuForVerification();
    }
}

/// <summary>停用与退出的统一保存预检：失败时不完成退出，宿主继续可用并可重试。</summary>
internal sealed class SuiteShutdown(SuiteController controller, Action shutdown, Action<string> report)
{
    private bool busy;
    public async Task<bool> TryExitAsync()
    {
        if (busy) return false;
        busy = true;
        try
        {
            if (!await PrepareAndStopAsync()) return false;
            shutdown();
            return true;
        }
        catch (Exception ex) { report(ex.Message); return false; }
        finally { busy = false; }
    }
    /// <summary>提交编辑、保存预检并释放模块；失败时返回 false 且保留可继续使用的运行状态。</summary>
    public async Task<bool> PrepareAndStopAsync()
    {
        if (!await controller.PrepareExitAsync()) return false;
        await controller.StopAsync();
        return true;
    }
}
