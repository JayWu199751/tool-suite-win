using System.IO;
using System.Windows;
using Pinto;
using Pinto.Core;
using Pinto.Views;
using ToolSuite.Core;
using SuiteTheme = ToolSuite.Core.ThemePreference;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetFullPath(args[0]), "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 0;
        app.Dispatcher.BeginInvoke(async () =>
        {
            IToolModule module = new PintoModule();
            SuiteController? controller = null;
            try
            {
                await module.StartAsync(new ModuleContext(root, SuiteTheme.Dark, message => Console.WriteLine(message)), default);
                var note = app.Windows.OfType<NoteWindow>().Single();
                Check("套件新数据创建独立空白便签", note.IsVisible && note.Model.Items.Count == 0 && note.Model.Title == "新便签");
                await module.Commands.Single(command => command.Id == "new").ExecuteAsync(default);
                Check("统一命令新建第二张便签", app.Windows.OfType<NoteWindow>().Count() == 2);
                await module.Commands.Single(command => command.Id == "hide-all").ExecuteAsync(default);
                Check("全部隐藏保留内容", app.Windows.OfType<NoteWindow>().All(window => !window.IsVisible));
                var hidden = module.Commands.Where(command => command.Id.StartsWith("show-note/")).ToArray();
                Check("统一托盘动态列出隐藏标题", hidden.Length == 2 && hidden.All(command => command.Label == "新便签"));
                await hidden[0].ExecuteAsync(default);
                Check("统一命令恢复指定便签", app.Windows.OfType<NoteWindow>().Count(window => window.IsVisible) == 1);
                await module.PrepareToStopAsync(default);
                using (var blockedFile = new FileStream(new DataStore(root).StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    note.Close();
                    Check("保存失败阻止隐藏，保留可恢复窗口", note.IsVisible);
                    var rejected = false;
                    try { await module.PrepareToStopAsync(default); } catch (IOException) { rejected = true; }
                    Check("保存失败阻止停用", rejected && note.IsVisible);
                }
                await module.PrepareToStopAsync(default);
                await module.StopAsync(default);
                Check("停止仅关闭便签并保留宿主", app.Windows.OfType<NoteWindow>().Count() == 0 && new DataStore(root).Load().Data.Notes.Count == 2);
                await module.StartAsync(new ModuleContext(root, SuiteTheme.Light, Console.WriteLine), default);
                var restored = app.Windows.OfType<NoteWindow>().Single(window => window.IsVisible);
                ((System.Windows.Controls.TextBox)restored.FindName("NewItemEditor")).Text = "停用前尚未提交的清单项";
                await module.PrepareToStopAsync(default);
                await module.StopAsync(default);
                await module.StartAsync(new ModuleContext(root, SuiteTheme.Light, Console.WriteLine), default);
                restored = app.Windows.OfType<NoteWindow>().Single(window => window.IsVisible);
                Check("停用提交新增草稿且重启保持隐藏状态", restored.Model.Items.Single().Text == "停用前尚未提交的清单项" && app.Windows.OfType<NoteWindow>().Count(window => !window.IsVisible) == 1);
                await module.PrepareToStopAsync(default);
                await module.StopAsync(default);
                var blockedRoot = Path.Combine(root, "blocked-data");
                File.WriteAllText(blockedRoot, "该路径为文件，不能写入便签");
                var startupRejected = false;
                try { await module.StartAsync(new ModuleContext(blockedRoot, SuiteTheme.Light, Console.WriteLine), default); }
                catch (IOException) { startupRejected = true; }
                Check("首次保存失败不伪装启动成功且清理窗口", startupRejected && !app.Windows.OfType<NoteWindow>().Any());
                var suiteStore = new SuiteSettingsStore(Path.Combine(root, "controller"));
                var preferences = new SuiteSettings();
                preferences.EnabledTools["pinto"] = false;
                suiteStore.Save(preferences);
                controller = new SuiteController(suiteStore, [new PintoModule()]);
                await controller.StartAsync();
                controller.ApplyEffectiveTheme(SuiteTheme.Dark);
                Check("冷启动停用便签与离线改主题不分配窗口，仍可恢复数据", !app.Windows.OfType<NoteWindow>().Any() && controller.Tools.Single(tool => tool.Id == "pinto").Commands.Any(command => command.Id == "recover" && !command.RequiresRunning));
                var sentinel = new ResourceDictionary { ["TextBrush"] = "宿主自身资源" };
                app.Resources.MergedDictionaries.Add(sentinel);
                Check("通过套件控制入口启用便签", await controller.SetEnabledAsync("pinto", true));
                var controlled = app.Windows.OfType<NoteWindow>().Single();
                controlled.Model.NewItemText = "控制入口保存的清单";
                controlled.Model.AddItem();
                controlled.Model.Accent = AccentColor.Rose;
                controller.ApplyEffectiveTheme(SuiteTheme.Light);
                controller.ApplyEffectiveTheme(SuiteTheme.Dark);
                Check("便签主题不替换宿主资源且强调色独立", ReferenceEquals(app.Resources.MergedDictionaries.Single(), sentinel) && Equals(app.FindResource("TextBrush"), "宿主自身资源") && controlled.FindResource("TextBrush") is System.Windows.Media.SolidColorBrush && controlled.Model.Accent == AccentColor.Rose);
                Check("套件退出预检保存成功", await controller.PrepareExitAsync());
                using (var blockedFile = new FileStream(new DataStore(suiteStore.ModuleDirectory("pinto")).StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Check("真实控制入口拒绝保存失败的停用", !await controller.SetEnabledAsync("pinto", false) && controller.Tools.Single(tool => tool.Id == "pinto").State == ToolState.Running && controller.Settings.EnabledTools["pinto"] && controlled.IsVisible);
                }
                Check("解除故障后可停用便签", await controller.SetEnabledAsync("pinto", false) && !app.Windows.OfType<NoteWindow>().Any());
                Check("通过套件重新启用便签", await controller.SetEnabledAsync("pinto", true));
                controlled = app.Windows.OfType<NoteWindow>().Single();
                Check("控制入口重启恢复清单与强调色", controlled.Model.Items.Single().Text == "控制入口保存的清单" && controlled.Model.Accent == AccentColor.Rose);
                Check("最终套件退出预检", await controller.PrepareExitAsync());
                await controller.StopAsync();
                Check("停止后窗口全部释放且仅有一个Application", !app.Windows.OfType<NoteWindow>().Any() && ReferenceEquals(Application.Current, app));
            }
            catch (Exception error) { Console.WriteLine(error); exitCode = 1; }
            finally { if (controller is not null) await controller.StopAsync(); await module.StopAsync(default); app.Shutdown(); }
        });
        app.Run();
        return exitCode;
    }
    private static void Check(string message, bool condition)
    {
        if (!condition) throw new Exception("失败：" + message);
        Console.WriteLine("通过：" + message);
    }
}






