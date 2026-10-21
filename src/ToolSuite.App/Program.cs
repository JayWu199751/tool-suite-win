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
    private SettingsWindow? settings;
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
            controller = new SuiteController(new SuiteSettingsStore(root), verify ? [] : ModuleCatalog.CreateModules());
            theme = new SuiteTheme(controller, Dispatcher);
            tray = new TrayHost(controller, ShowSettings, RequestExitAsync);
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
        if (exiting) return;
        if (settings is null)
        {
            settings = new SettingsWindow(controller!);
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Activate();
    }
    private async Task RequestExitAsync()
    {
        if (exiting || controller is null) return;
        exiting = true;
        try
        {
            if (!await controller.PrepareExitAsync()) { exiting = false; return; }
            await controller.StopAsync();
            settings?.Close();
            Shutdown();
        }
        catch (Exception ex) { exiting = false; tray?.ShowError(ex.Message); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        theme?.Dispose(); tray?.Dispose();
        base.OnExit(e);
    }
    private async Task VerifyAsync()
    {
        // 通过真实托盘菜单控件事件与真实设置窗口检查，不通过私有模块状态模拟用户结果。
        if (Windows.Count != 0) throw new Exception("空白启动显示了首页。");
        tray!.InvokeSettingsMenuForVerification();
        if (settings is null || !settings.IsVisible) throw new Exception("托盘设置命令未显示窗口。");
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
        for (var i = 0; i < 30 && settings is null; i++) await Task.Delay(50);
        if (settings is null || !settings.IsVisible) throw new Exception("重复启动没有通知原实例显示设置。");
        settings.Close();
        File.WriteAllText(Path.Combine(root, "verification-passed.txt"),
            "空白启动无首页；托盘设置命令；设置关闭释放；重复实例退出且原实例收到通知；安全退出。\n" + DateTimeOffset.Now);
        tray.InvokeExitMenuForVerification();
    }
}
