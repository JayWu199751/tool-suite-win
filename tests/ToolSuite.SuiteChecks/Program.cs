using System.IO;
using System.Windows;
using ToolSuite.Core;
using Application = System.Windows.Application;

namespace ToolSuite.SuiteChecks;

/// <summary>宿主级整合检查：真实 SuiteController/TrayHost/SettingsWindow 公开入口、
/// 真实三个工具与隔离数据根；真实 WPF STA 运行，不读取私有字段或模块内部状态。</summary>
internal static class Program
{
    private static Application app = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        app.Resources["suite-check-sentinel"] = "host resources stay intact";
        var root = Path.GetFullPath(args[0]);
        var exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                var log = new List<string>();
                await UnifiedSettingsChecks.RunAsync(Path.Combine(root, "unified-settings"), log);
                if (!args.Contains("--unified-settings"))
                {
                    ContractSingleSourceChecks.Run(Path.Combine(root, "single-source"), log);
                    await ControllerSemanticsChecks.RunAsync(Path.Combine(root, "controller"), log);
                    await HostIntegrationChecks.RunAsync(Path.Combine(root, "host"), log);
                }
                foreach (var line in log) Console.WriteLine("PASS " + line);
                Console.WriteLine("PASS 工单 06 宿主级整合检查全部通过");
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                exit = 1;
            }
            finally { app.Shutdown(); }
        };
        app.Run();
        return exit;
    }
}
