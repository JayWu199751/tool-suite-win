using System.Diagnostics;
using System.Reflection;
using ToolSuite.Core;

namespace ToolSuite.Installer;

internal static class Program
{
    private const string PayloadResource = "ToolSuite.Installer.payload.zip";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = InstallerOptions.Parse(args);
            ApplicationConfiguration.Initialize();
            return options.Silent ? RunSilent(options) : RunInteractive(options);
        }
        catch (Exception ex)
        {
            if (args.Any(x => x.Equals("/silent", StringComparison.OrdinalIgnoreCase)))
                Console.Error.WriteLine(ex.Message);
            else MessageBox.Show(ex.Message, "工具套件安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunSilent(InstallerOptions options)
    {
        try
        {
            Execute(options, message => Console.Out.WriteLine(message));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex is InstallerBlockedException ? 2 : 1;
        }
    }

    private static int RunInteractive(InstallerOptions options)
    {
        Application.Run(new InstallerForm(options));
        return 0;
    }

    internal static string Execute(InstallerOptions options, Action<string> report)
    {
        var engine = new InstallerEngine(new WindowsRegistryAccess(), new ShellShortcutWriter(),
            Environment.ProcessPath, SuiteIdentity.Version, new ToolSuite.Windows.LogonTaskStore());
        if (options.Mode == InstallerMode.Uninstall)
        {
            var install = Path.GetFullPath(options.InstallDirectory);
            // 卸载入口自身位于安装目录内时无法删除正在运行的 EXE：先清理其余文件与注册项，
            // 再由独立 cmd 在进程退出后删除目录，避免影响单文件应用的运行时加载。
            var runningSetup = Environment.ProcessPath is { } self
                && string.Equals(Path.GetDirectoryName(self)?.TrimEnd(Path.DirectorySeparatorChar),
                    install.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                ? self : null;
            engine.Uninstall(install, runningSetup);
            if (runningSetup is not null) ScheduleDelete(install);
            report($"已卸载工具套件，用户数据保留在 %LOCALAPPDATA%\\{SuiteIdentity.DataFolderName}。");
            return "卸载完成";
        }
        var result = engine.Install(OpenPayload, options.InstallDirectory, options.StartMenu, options.Desktop);
        var message = result.Upgraded
            ? $"已升级到 {result.Version}：{result.InstallDirectory}"
            : $"已安装 {result.Version}：{result.InstallDirectory}";
        report(message + "。开机启动默认关闭，可在套件设置中开启。");
        return message;
    }

    internal static Stream OpenPayload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("安装包缺少程序载荷，请使用 scripts/build-installer.ps1 重新构建。");
    }

    /// <summary>进程退出后删除保留的安装目录（其中只剩正在运行的卸载入口）。</summary>
    private static void ScheduleDelete(string path)
    {
        try
        {
            // cmd 的原生参数使用双引号重复表示内层引号，避免路径中的空格被截断。
            var start = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = "/c \"choice /t 2 /d y /n >nul & rmdir /s /q \"\"" + path + "\"\"\""
            };
            Process.Start(start)?.Dispose();
        }
        catch (Exception) { /* 清理失败不影响卸载结果。 */ }
    }
}
