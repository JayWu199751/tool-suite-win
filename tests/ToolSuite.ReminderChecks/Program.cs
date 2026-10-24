using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToolSuite.Core;
using ToolSuite.Reminders;

namespace ToolSuite.ReminderChecks;

internal static class Program
{
    private static int checks;
    [STAThread]
    public static int Main(string[] args)
    {
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/reminder-checks");
        Directory.CreateDirectory(root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                ScheduleChecks(Path.Combine(root, "schedule"));
                FailureChecks(Path.Combine(root, "failures"));
                ContrastChecks();
                await DesktopChecks(root);
                Console.WriteLine($"PASS 循环提醒：{checks} 项检查通过；预览保存在 {root}");
            }
            catch (Exception error) { Console.Error.WriteLine(error); exit = 1; }
            finally { foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close(); app.Shutdown(); }
        };
        app.Run();
        return exit;
    }

    private static void ScheduleChecks(string root)
    {
        long now = 0;
        var schedule = new ReminderSchedule(root, () => now);
        Check(schedule.Reminders.Count == 0, "首次没有提醒");
        var first = schedule.Save(null, "喝水", 30, true);
        var second = schedule.Save(null, "活动", 60, true);
        var paused = schedule.Save(null, "休息", 10, false);
        schedule.Start();
        now = 30_000; schedule.Tick();
        Check(schedule.Pending.Select(r => r.Id).SequenceEqual([first.Id]), "各项独立到期，停用项不提醒");
        now = 600_000; schedule.Tick(); schedule.Tick();
        Check(schedule.Pending.Count == 2, "跨多个周期仅各提醒一次，不叠加");
        schedule.Acknowledge([first.Id]);
        Check(schedule.Remaining(first.Id) == TimeSpan.FromSeconds(30) && schedule.Pending.Single().Id == second.Id,
            "从确认时刻重新计时，保留其他待确认项");
        now += 5_000;
        schedule.Save(first.Id, "多喝水", 30, true);
        Check(schedule.Remaining(first.Id) == TimeSpan.FromSeconds(25), "改名不重计时");
        schedule.Save(first.Id, "多喝水", 40, true);
        Check(schedule.Remaining(first.Id) == TimeSpan.FromSeconds(40), "改间隔重计时");
        schedule.Save(second.Id, "活动", 60, false);
        Check(schedule.Pending.Count == 0 && schedule.Remaining(second.Id) is null, "停用移除待确认项");
        schedule.Save(paused.Id, "休息", 10, true);
        Check(schedule.Remaining(paused.Id) == TimeSpan.FromSeconds(10), "单项启用从完整间隔开始");
        now += 40_000; schedule.Tick();
        schedule.Delete(first.Id);
        Check(schedule.Pending.All(r => r.Id != first.Id) && schedule.Reminders.All(r => r.Id != first.Id), "删除移除配置和待确认项");
        schedule.Stop(); now += 1_000_000; schedule.Tick();
        Check(schedule.Pending.Count == 0, "停止后无调度");
        var restarted = new ReminderSchedule(root, () => now);
        restarted.Start();
        Check(restarted.Reminders.Count == 2 && restarted.Reminders.Single(r => r.Id == second.Id).Enabled == false
            && restarted.Remaining(paused.Id) == TimeSpan.FromSeconds(10), "重启恢复配置和启停状态，从完整间隔开始");
        restarted.Stop();
        Check(restarted.Remaining(paused.Id) is null, "重复停止与状态清理");
    }

    private static void FailureChecks(string root)
    {
        long now = 0;
        var schedule = new ReminderSchedule(root, () => now);
        var item = schedule.Save(null, "保留原配置", 10, true);
        schedule.Start(); now = 3_000;
        var temporary = Path.Combine(root, "reminders.json.tmp");
        Directory.CreateDirectory(temporary);
        Throws(() => schedule.Save(item.Id, "不应生效", 99, false), "写入失败应报错");
        Check(schedule.Reminders.Single() == item && schedule.Remaining(item.Id) == TimeSpan.FromSeconds(7), "写入失败保留原配置和原计时");
        Throws(() => schedule.Delete(item.Id), "删除持久化失败应报错");
        Check(schedule.Reminders.Single() == item, "删除失败保留原配置");
        Directory.Delete(temporary);
        Throws(() => schedule.Save(null, " ", 1, true), "空名称被拒绝");
        Throws(() => schedule.Save(null, "无效", 0, true), "零间隔被拒绝");
        File.WriteAllText(Path.Combine(root, "reminders.json"), "{broken");
        Throws(() => new ReminderSchedule(root).Start(), "损坏文件明确报错");
        Check(File.ReadAllText(Path.Combine(root, "reminders.json")) == "{broken", "损坏原文件保持不变");
    }

    private static async Task DesktopChecks(string root)
    {
        var module = new ReminderModule();
        var errors = new List<string>();
        var requested = "";
        var context = new ModuleContext(Path.Combine(root, "module"), ThemePreference.Light, errors.Add,
            RequestSettings: id => requested = id);
        module.Configure(context);
        var management = module.Commands.Single(c => c.Id == "settings");
        await management.ExecuteAsync(CancellationToken.None);
        Check(!management.RequiresRunning && requested == ToolIds.Reminders && !module.Schedule.IsRunning,
            "停用时管理命令定位设置，不隐式启动");
        requested = "";
        var createCommand = module.Commands.Single(c => c.Id == "new");
        await createCommand.ExecuteAsync(CancellationToken.None);
        var standalone = Application.Current.Windows.OfType<ReminderEditorWindow>().Single();
        standalone.UpdateLayout();
        Check(!createCommand.RequiresRunning && requested == "" && !module.Schedule.IsRunning,
            "托盘新增命令直接打开独立窗口，不调用设置或启用工具");
        var draftName = Descendants(standalone).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "提醒名称");
        draftName.Text = "保留草稿";
        await createCommand.ExecuteAsync(CancellationToken.None);
        Check(Application.Current.Windows.OfType<ReminderEditorWindow>().Single() == standalone && draftName.Text == "保留草稿",
            "重复点击新增复用窗口并保留草稿");
        Click(standalone, "取消");
        Check(module.Schedule.Reminders.Count == 0 && !Application.Current.Windows.OfType<ReminderEditorWindow>().Any(),
            "取消关闭独立窗口，不创建提醒");
        var control = new ReminderSettingsControl(module);
        var view = new Window { Title = "循环提醒设置检查", Width = 360, Height = 700, ShowActivated = false,
            Content = new ScrollViewer { Content = control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 247)) };
        view.Show(); view.UpdateLayout();
        try
        {
            Click(control, "新增提醒…"); view.UpdateLayout();
            var editor = Application.Current.Windows.OfType<ReminderEditorWindow>().Single();
            editor.UpdateLayout();
            Check(!Descendants(control).OfType<TextBox>().Any() && editor.Owner is null,
                "管理列表不再包含编辑表单，新增窗口独立于设置");
            var inputs = Descendants(editor).OfType<TextBox>().ToArray();
            inputs.Single(x => AutomationProperties.GetName(x) == "提醒名称").Text = "喝水，放松一下";
            inputs.Single(x => AutomationProperties.GetName(x) == "提醒间隔数值").Text = "0";
            Click(editor, "创建提醒");
            Check(module.Schedule.Reminders.Count == 0 && Descendants(editor).OfType<TextBlock>().Any(x => x.Text.Contains("大于零")),
                "真实表单拒绝零间隔并就地提示");
            inputs.Single(x => AutomationProperties.GetName(x) == "提醒间隔数值").Text = "30";
            module.ApplyTheme(ThemePreference.Light);
            Render((FrameworkElement)editor.Content, Path.Combine(root, "reminders-editor-light.png"), 438);
            module.ApplyTheme(ThemePreference.Dark);
            Render((FrameworkElement)editor.Content, Path.Combine(root, "reminders-editor-dark.png"), 438);
            var failedSavePath = Path.Combine(context.DataDirectory, "reminders.json.tmp");
            Directory.CreateDirectory(failedSavePath);
            Click(editor, "创建提醒");
            Check(editor.IsVisible && module.Schedule.Reminders.Count == 0 && inputs[0].Text == "喝水，放松一下",
                "独立窗口保存失败保留草稿与窗口");
            Directory.Delete(failedSavePath);
            Click(editor, "创建提醒");
            Check(module.Schedule.Reminders.Single().IntervalSeconds == 1800 && !module.Schedule.IsRunning
                && !Application.Current.Windows.OfType<ReminderEditorWindow>().Any(), "独立表单离线创建并关闭，分钟单位正确");
            var item = module.Schedule.Reminders.Single();
            module.Schedule.Save(null, "站起来活动一会儿", 3600, true);
            module.Schedule.Save(null, "休息，看看窗外", 600, false);
            await module.StartAsync(context, CancellationToken.None);
            Check(module.Schedule.IsRunning, "模块启动调度");
            module.ApplyTheme(ThemePreference.Light);
            Render(control, Path.Combine(root, "reminders-settings-light.png"), 320);
            module.ApplyTheme(ThemePreference.Dark);
            Render(control, Path.Combine(root, "reminders-settings-dark.png"), 320);
            Click(control, "编辑"); view.UpdateLayout();
            editor = Application.Current.Windows.OfType<ReminderEditorWindow>().Single();
            editor.UpdateLayout();
            var editorBody = (StackPanel)((ScrollViewer)editor.Content).Content;
            editorBody.LayoutTransform = new ScaleTransform(2, 2);
            Render((FrameworkElement)editor.Content, Path.Combine(root, "reminders-editor-large-text.png"), 438);
            Check(Descendants(editor).OfType<TextBox>().All(x => x.ActualWidth > 0 && x.ActualHeight >= 32), "200% 文字下编辑框仍可布局和操作");
            editorBody.LayoutTransform = Transform.Identity;
            inputs = Descendants(editor).OfType<TextBox>().ToArray();
            inputs.Single(x => AutomationProperties.GetName(x) == "提醒名称").Text = "带上水杯";
            Click(editor, "保存修改");
            Check(module.Schedule.Reminders.Single(r => r.Id == item.Id).Name == "带上水杯", "真实表单编辑并保存");
            Render(control, Path.Combine(root, "reminders-settings-compact.png"), 240);

            // 真实短计时只等待一次；其余边界均由可控计时源验证。
            module.Schedule.Save(null, "短时提醒甲", 1, true);
            module.Schedule.Save(null, "短时提醒乙", 1, true);
            await Task.Delay(2300);
            Check(module.Schedule.Pending.Count == 2, "真实 DispatcherTimer 到期，两个提醒聚合");
            var popups = Application.Current.Windows.OfType<ReminderWindow>().ToArray();
            if (popups.Length > 0)
            {
                Check(popups.Length == 1 && popups[0].Topmost && !popups[0].ShowActivated, "真实模块只创建一个置顶且不主动激活的弹窗");
                popups[0].Close();
                Check(module.Schedule.Pending.Count == 0 && !Application.Current.Windows.OfType<ReminderWindow>().Any(), "关闭聚合弹窗即全部确认");
                await Task.Delay(2300);
                Check(module.Schedule.Pending.Count == 2 && Application.Current.Windows.OfType<ReminderWindow>().Count() == 1,
                    "全部确认后重新计时并只重建一个弹窗");
            }
            else Console.WriteLine("人工边界：当前输入桌面不可用，模块延迟呈现；直接渲染窗口验证布局。");
            await module.StopAsync(CancellationToken.None);
            Check(module.Schedule.Pending.Count == 0 && !Application.Current.Windows.OfType<ReminderWindow>().Any(), "停用释放聚合弹窗与计时");
            await module.StopAsync(CancellationToken.None);
        }
        finally { view.Close(); await module.StopAsync(CancellationToken.None); }

        long now = 0;
        var schedule = new ReminderSchedule(Path.Combine(root, "popup"), () => now);
        schedule.Save(null, "喝水，放松一下", 1800, true);
        schedule.Save(null, "站起来活动一会儿", 3600, true);
        schedule.Start(); now = 3_600_000; schedule.Tick();
        var popup = new ReminderWindow(schedule, ThemePreference.Light);
        popup.Show(); popup.UpdateLayout();
        Render((FrameworkElement)popup.Content, Path.Combine(root, "reminders-popup-light.png"), 378);
        popup.ApplyTheme(ThemePreference.Dark);
        Render((FrameworkElement)popup.Content, Path.Combine(root, "reminders-popup-dark.png"), 378);
        var confirm = Descendants(popup).OfType<Button>().First(b => Equals(b.Content, "知道了"));
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(schedule.Pending.Count == 1, "真实弹窗按钮逐项确认");
        popup.Close();
        // 窗口关闭语义由模块订阅处理，上面已检查停止不会错误确认。
        Check(errors.Count == 0, "正常生命周期无错误报告");
    }

    private static void ContrastChecks()
    {
        foreach (var (foreground, background) in new[] { ("#63636A", "#FFFFFF"), ("#63636A", "#F5F5F7"),
            ("#0066CC", "#FFFFFF"), ("#0066CC", "#E8E8ED"), ("#A0A0AC", "#141419"), ("#A0A0AC", "#0B0B0E"),
            ("#A7A4F0", "#141419") })
        {
            var a = Luminance((Color)ColorConverter.ConvertFromString(foreground));
            var b = Luminance((Color)ColorConverter.ConvertFromString(background));
            var ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            Check(ratio >= 4.5, $"颜色 {foreground}/{background} 对比度 {ratio:F2}:1");
        }
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }

    private static void Render(FrameworkElement element, string path, double width)
    {
        element.Measure(new Size(width, 1800));
        var height = Math.Min(1800, Math.Max(1, element.DesiredSize.Height));
        element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Click(DependencyObject root, string content)
        => Descendants(root).OfType<Button>().First(b => Equals(b.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Throws(Action action, string message)
    { try { action(); } catch { Check(true, message); return; } throw new Exception("FAIL " + message); }
    private static void Check(bool result, string message)
    { if (!result) throw new Exception("FAIL " + message); checks++; Console.WriteLine("PASS " + message); }
}
