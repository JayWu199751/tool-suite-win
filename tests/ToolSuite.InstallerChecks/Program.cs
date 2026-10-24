using System.IO;
using System.Windows;
using ToolSuite.Core;
using Application = System.Windows.Application;

namespace ToolSuite.InstallerChecks;

/// <summary>工单 08 的隔离检查：可控自启存储、临时安装目录与可控卸载注册表。
/// 不修改真实开机启动项、真实安装路径或真实卸载信息；原生接口仅查询和删除不存在的隔离任务。
/// 真实 UAC/登录场景只写人工步骤。</summary>
internal static class Program
{
    private static Application app = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var root = Path.GetFullPath(args[0]);
        var exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                var log = new List<string>();
                AutoStartChecks.Run(Path.Combine(root, "autostart"), log);
                if (!args.Contains("--autostart-only"))
                {
                    await AutoStartChecks.RunStartupSelectionAsync(Path.Combine(root, "startup"), log);
                    InstallerChecks.Run(Path.Combine(root, "installer"), log);
                }
                foreach (var line in log) Console.WriteLine("PASS " + line);
                Console.WriteLine("PASS 工单 08 自启与安装器隔离检查全部通过");
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
