using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ToolSuite.App;
using ToolSuite.Core;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;
using Color = System.Drawing.Color;

namespace ToolSuite.SuiteChecks;

/// <summary>真实三模块 + 真实托盘/设置入口的宿主整合检查，全部使用隔离数据根。</summary>
internal static class HostIntegrationChecks
{
    internal static async Task RunAsync(string root, List<string> log)
    {
        await TrayAndSettingsAsync(Path.Combine(root, "suite"), log);
        await VisibilityAndRestartAsync(Path.Combine(root, "visibility"), log);
        await SaveFailureAsync(Path.Combine(root, "save-failure"), log);
        await StartFailureRetryAsync(Path.Combine(root, "start-failure"), log);
    }

    /// <summary>唯一托盘、分组命令、启停互不影响、全部停用后可恢复、设置按需创建与释放。</summary>
    private static async Task TrayAndSettingsAsync(string root, List<string> log)
    {
        var store = new SuiteSettingsStore(root);
        var controller = new SuiteController(store, ModuleCatalog.CreateModules());
        var errors = new List<string>();
        controller.ErrorReported += errors.Add;
        var settings = new SettingsHost(controller);
        using var tray = new TrayHost(controller, () => settings.Show(), () => Task.CompletedTask);
        // 托盘图标必须来自应用资源（多尺寸 ICO 的托盘帧），缺失时会静默退回系统默认图标。
        var trayIcon = tray.TrayIconForVerification;
        Assert(trayIcon is not null, "托盘图标未从 Assets/app.ico 加载");
        Assert(trayIcon!.Width == Forms.SystemInformation.SmallIconSize.Width,
            $"托盘图标应取系统小图标尺寸（{trayIcon.Width} vs {Forms.SystemInformation.SmallIconSize.Width}）");
        using var theme = new SuiteTheme(controller, Application.Current.Dispatcher);
        theme.AppearanceApplied += tray.ApplyAppearance;
        try
        {
            await controller.StartAsync();
            tray.RefreshMenuForVerification();
            AssertThreeGroups(tray);
            AssertTrayMenuSkin(tray);
            AssertClipboardGroupHasNoSummon(tray);
            Assert(controller.Tools.All(x => x.State == ToolState.Running), "三个真实工具应全部运行：" + Describe(controller));
            log.Add("单一托盘按剪贴板/性能条/便签分组展示状态与动作，剪贴板无呼出面板动作");

            // 点击外部收起：菜单打开后前台必须转到自己的弹出层，否则点桌面/其他窗口时菜单不收起。
            tray.OpenMenuForVerification();
            var popup = IntPtr.Zero;
            await WaitUntil(() => (popup = tray.MenuPopupHandleForVerification) != IntPtr.Zero, "托盘菜单弹出层就绪");
            await Task.Delay(50); // 等 OnOpened 里的前台转移生效
            Assert(NativeMethods.GetForegroundWindow() == popup,
                "托盘菜单打开后前台必须落在菜单弹出层（点击外部收起的前提）");
            tray.MenuForVerification.IsOpen = false;
            // 防闪重开：被这记右键顶下的菜单不得立刻重开；程序化打开与早已收起的右键不受影响。
            var now = Environment.TickCount64;
            Assert(TrayHost.ShouldOpenAfterRightClick(TrayMenu.Never, now), "从未收起过的菜单右键必须打开");
            Assert(TrayHost.ShouldOpenAfterRightClick(now - 5000, now), "早已收起的菜单右键必须重新打开");
            Assert(!TrayHost.ShouldOpenAfterRightClick(now, now), "右键按下瞬间被顶下的菜单不得重开");
            Assert(!TrayHost.ShouldOpenAfterRightClick(now, now - 50), "收起晚于按下（消息乱序）同样不得重开");
            Assert(!TrayHost.ShouldOpenAfterRightClick(now - 40, now), "收起略早于按下（乱序容差内）也不得重开");
            Assert(TrayHost.ShouldOpenAfterRightClick(now - 5000, TrayMenu.Never), "程序化打开不得被防闪重开拦截");
            log.Add("托盘菜单打开即接管前台，点击外部可收起；再次右键只收起不重开");

            // 停用一个工具，另外两个继续运行。
            ClickMenuItem(Toggle(tray, "pinto"));
            await WaitUntil(() => StateOf(controller, "pinto") == ToolState.Disabled, "便签停用");
            Assert(StateOf(controller, "clipboard") == ToolState.Running && StateOf(controller, "perf-monitor") == ToolState.Running,
                "停用便签不得影响剪贴板与性能条：" + Describe(controller));
            log.Add("停用便签后剪贴板与性能条继续运行");

            // 统一设置按需创建、反映状态、关闭释放；设置内启停与主题不隐式启用其他停用工具。
            tray.InvokeSettingsMenuForVerification();
            var window = settings.Current ?? throw new Exception("托盘统一设置命令未创建设置窗口");
            Assert(window.IsVisible, "统一设置应可见");
            Assert(window.ToolToggleForVerification("pinto") is { IsChecked: false }, "设置应反映便签已停用");
            window.ThemeSelectorForVerification.SelectedIndex = (int)ThemePreference.Dark;
            await Task.Delay(50);
            Assert(StateOf(controller, "pinto") == ToolState.Disabled, "修改主题不得隐式启用已停用工具");
            Assert(controller.Tools.Single(x => x.Id == "pinto").Enabled == false, "设置操作不得隐式改写启停偏好");
            ClickToggle(window, "pinto", true);
            await WaitUntil(() => StateOf(controller, "pinto") == ToolState.Running, "设置内重新启用便签");
            ClickToggle(window, "pinto", false);
            await WaitUntil(() => StateOf(controller, "pinto") == ToolState.Disabled, "设置内停用便签");
            settings.Close();
            Assert(settings.Current is null && !Application.Current.Windows.Cast<Window>().Contains(window.View), "设置关闭后必须释放窗口");
            log.Add("统一设置按需创建、关闭释放；主题与启停操作不隐式启用停用工具");

            // 全部停用后托盘与设置仍可用，可重新启用任意工具。
            foreach (var id in new[] { "clipboard", "perf-monitor", "pinto" })
                if (StateOf(controller, id) == ToolState.Running) { ClickMenuItem(Toggle(tray, id)); await WaitUntil(() => StateOf(controller, id) == ToolState.Disabled, id + " 停用"); }
            await WaitUntil(() => controller.Tools.All(x => x.State != ToolState.Running), "全部停用");
            tray.RefreshMenuForVerification();
            AssertThreeGroups(tray);
            AssertTrayMenuSkin(tray);
            Assert(controller.Tools.All(x => Toggle(tray, x.Id).IsEnabled), "全部停用后各分组启停动作仍可用");
            Assert(await controller.ExecuteCommandAsync("perf-monitor", "settings"), "停用工具应能打开离线设置");
            Assert(StateOf(controller, "perf-monitor") == ToolState.Disabled, "打开离线设置不得隐式启用停用工具");
            tray.InvokeSettingsMenuForVerification();
            var reopened = settings.Current ?? throw new Exception("全部停用后设置入口不可用");
            ClickToggle(reopened, "clipboard", true);
            await WaitUntil(() => StateOf(controller, "clipboard") == ToolState.Running, "全部停用后从设置重新启用剪贴板");
            settings.Close();
            log.Add("三个工具全部停用后唯一托盘与设置入口仍可重新启用任意工具");

            // 主题切换：有效外观变化一次下发，模块继续运行，主题不串扰启停状态。
            var applies = 0;
            theme.AppearanceApplied += (_, _) => applies++;
            var accents = PintoAccents(store.ModuleDirectory("pinto"));
            var previous = theme.IsDark ? ThemePreference.Light : ThemePreference.Dark;
            controller.SetTheme(previous);
            await Task.Delay(700);
            Assert(applies == 1, $"一次有效外观变化应只刷新一次（实际 {applies}）");
            Assert(StateOf(controller, "pinto") == ToolState.Disabled && StateOf(controller, "clipboard") == ToolState.Running,
                "主题切换不得改变工具启停状态：" + Describe(controller));
            var menu = tray.MenuForVerification;
            // 托盘浮层必须落在设计 token 上（docs/design-system.md：Paper #141419/#FFFFFF，Ink #F2F2F5/#1D1D1F）；
            // 高对比度交还系统色。菜单是 WPF ContextMenu，画刷走 DynamicResource 跟随 Suite* 键。
            if (previous == ThemePreference.Dark)
            {
                Assert(Argb(menu.Background) == Color.FromArgb(0x14, 0x14, 0x19).ToArgb(), $"暗色托盘浮层应为 #141419（实际 {Argb(menu.Background):X8}）");
                Assert(Argb(menu.Foreground) == Color.FromArgb(0xF2, 0xF2, 0xF5).ToArgb(), $"暗色托盘正文应为 #F2F2F5（实际 {Argb(menu.Foreground):X8}）");
            }
            else
            {
                Assert(Argb(menu.Background) == Color.White.ToArgb(), $"亮色托盘浮层应为 #FFFFFF（实际 {Argb(menu.Background):X8}）");
                Assert(Argb(menu.Foreground) == Color.FromArgb(0x1D, 0x1D, 0x1F).ToArgb(), $"亮色托盘正文应为 #1D1D1F（实际 {Argb(menu.Foreground):X8}）");
            }
            tray.ApplyAppearance(false, true);
            Assert(Argb(menu.Background) == System.Drawing.SystemColors.Window.ToArgb()
                    && Argb(menu.Foreground) == System.Drawing.SystemColors.WindowText.ToArgb(),
                "高对比度托盘必须交还系统配色");
            tray.ApplyAppearance(theme.IsDark, false);
            await controller.ExecuteCommandAsync("perf-monitor", "visibility");
            await Task.Delay(50);
            Assert(applies == 1, "普通命令不得重复刷新外观");
            await controller.ExecuteCommandAsync("perf-monitor", "visibility");
            Assert(!controller.Tools.Any(x => x.Commands.Any(c => c.Label == "退出")),
                "模块命令不得提供会关闭整个 Application 的退出入口");
            Assert(!Application.Current.Dispatcher.HasShutdownStarted, "模块操作不得关闭宿主 Application");
            Assert(PintoAccents(store.ModuleDirectory("pinto")) == accents, "主题切换不得改变便签强调色");
            Assert(Equals(Application.Current.Resources["suite-check-sentinel"], "host resources stay intact"),
                "模块局部主题不得替换宿主资源字典");
            log.Add("一次有效外观变化只刷新一次；暗色/高对比度托盘配色可读；便签强调色与宿主资源不受主题串扰");
        }
        finally
        {
            settings.Close();
            await controller.StopAsync();
        }
        Assert(!Application.Current.Dispatcher.HasShutdownStarted, "套件应保持运行直到显式退出");
    }

    /// <summary>启停偏好与窗口可见状态分别持久化：隐藏便签与隐藏性能条在停用/重启后保持隐藏。</summary>
    private static async Task VisibilityAndRestartAsync(string root, List<string> log)
    {
        var store = new SuiteSettingsStore(root);
        var controller = new SuiteController(store, ModuleCatalog.CreateModules());
        try
        {
            await controller.StartAsync();
            // 首次运行已有一张可见便签；新建后全部隐藏，再停用/启用验证隐藏状态保持。
            await controller.ExecuteCommandAsync("pinto", "hide-all");
            var hidden = HiddenNotes(controller);
            Assert(hidden == 1, $"隐藏全部后应只剩隐藏便签命令（实际 {hidden}）");
            await controller.SetEnabledAsync("pinto", false);
            await controller.SetEnabledAsync("pinto", true);
            Assert(HiddenNotes(controller) == hidden, "重新启用后已隐藏便签必须保持隐藏");
            await controller.ExecuteCommandAsync("pinto", "show-all");
            Assert(HiddenNotes(controller) == 0, "显示全部后不应再有隐藏便签命令");
            await controller.SetEnabledAsync("pinto", false);
            await controller.SetEnabledAsync("pinto", true);
            Assert(HiddenNotes(controller) == 0, "重新启用后可见便签必须保持可见");

            // 性能条隐藏状态由模块独立持久化。
            var label = VisibilityLabel(controller);
            await controller.ExecuteCommandAsync("perf-monitor", "visibility");
            var hiddenLabel = VisibilityLabel(controller);
            Assert(hiddenLabel != label, "性能条显隐命令应切换标签");
            await controller.SetEnabledAsync("perf-monitor", false);
            await controller.SetEnabledAsync("perf-monitor", true);
            Assert(VisibilityLabel(controller) == hiddenLabel, "重新启用后性能条应保持停用前的隐藏状态");
            await controller.ExecuteCommandAsync("perf-monitor", "visibility");
            log.Add("隐藏便签与隐藏性能条在停用/重新启用后保持；已显示便签保持显示");

            // 停用偏好重启保持：停用便签后重新创建控制器（同一数据根）不得隐式启用。
            controller.SetTheme(ThemePreference.Dark);
            await controller.SetEnabledAsync("pinto", false);
            Assert(!store.Load().EnabledTools["pinto"], "停用偏好必须立即持久化");
            await controller.StopAsync();
            var restarted = new SuiteController(new SuiteSettingsStore(root), ModuleCatalog.CreateModules());
            try
            {
                await restarted.StartAsync();
                Assert(StateOf(restarted, "pinto") == ToolState.Disabled, "重启后便签应保持停用");
                Assert(restarted.Settings.Theme == ThemePreference.Dark, "重启后应记住主题偏好");
                Assert(StateOf(restarted, "perf-monitor") == ToolState.Running && StateOf(restarted, "clipboard") == ToolState.Running,
                    "重启后其余工具应恢复运行：" + Describe(restarted));
            }
            finally { await restarted.StopAsync(); }
            log.Add("启停偏好与窗口可见状态分别持久化，重启保持");
        }
        finally { await controller.StopAsync(); }
    }

    /// <summary>停用与退出先保存预检；保存失败不完成停用、不退出、不丢内容，修复后可重试。</summary>
    private static async Task SaveFailureAsync(string root, List<string> log)
    {
        var store = new SuiteSettingsStore(root);
        var initial = new SuiteSettings();
        initial.EnabledTools["clipboard"] = false;
        initial.EnabledTools["perf-monitor"] = false;
        store.Save(initial);
        var controller = new SuiteController(store, ModuleCatalog.CreateModules());
        var exited = false;
        using var tray = new TrayHost(controller, () => { }, () => Task.CompletedTask);
        var shutdown = new SuiteShutdown(controller, () => exited = true, tray.ShowError);
        try
        {
            await controller.StartAsync();
            await controller.ExecuteCommandAsync("pinto", "new");
            await Task.Delay(700); // 等待模块自动保存落盘
            var notesBefore = CountNotes(store.ModuleDirectory("pinto"));
            // 真实文件系统拒绝写入：state.json 同名目录使原子替换失败。
            var statePath = Path.Combine(store.ModuleDirectory("pinto"), "state.json");
            File.Delete(statePath);
            Directory.CreateDirectory(statePath);
            Assert(!await controller.SetEnabledAsync("pinto", false), "保存预检失败必须拒绝停用");
            Assert(StateOf(controller, "pinto") == ToolState.Running, "保存失败不得伪装为已停用");
            Assert(controller.Tools.Single(x => x.Id == "pinto").Enabled, "保存失败不得改写启停偏好");
            Assert(!await shutdown.TryExitAsync() && !exited, "保存失败不得静默退出套件");
            Assert(!await shutdown.PrepareAndStopAsync(), "系统会话结束路径同样不得跳过保存预检");
            Assert(StateOf(controller, "pinto") == ToolState.Running, "退出失败后工具应继续可用");
            Assert(controller.Tools.Single(x => x.Id == "pinto").Commands.Any(c => c.Id == "new"),
                "退出失败后工具命令应仍可用");
            log.Add("停用与退出先保存预检；保存失败不完成停用、不退出且可继续使用");

            Directory.Delete(statePath);
            Assert(await controller.SetEnabledAsync("pinto", false), "修复存储后停用应可重试");
            Assert(await controller.SetEnabledAsync("pinto", true), "停用后应可重新启用");
            Assert(CountNotes(store.ModuleDirectory("pinto")) >= notesBefore, "保存失败期间不得丢弃已保存便签");
            log.Add("修复存储后停用/启用可重试，便签内容未丢失");
            Assert(await shutdown.TryExitAsync() && exited, "修复保存后退出应完成");
        }
        finally
        {
            var broken = Path.Combine(store.ModuleDirectory("pinto"), "state.json");
            if (Directory.Exists(broken)) Directory.Delete(broken, recursive: true);
            await controller.StopAsync();
        }
    }

    /// <summary>单模块启动失败：可操作提示、允许重试、不伪装运行、不影响其他模块。</summary>
    private static async Task StartFailureRetryAsync(string root, List<string> log)
    {
        var store = new SuiteSettingsStore(root);
        var modules = Path.Combine(root, "modules");
        Directory.CreateDirectory(modules);
        var blocked = Path.Combine(modules, "pinto");
        File.WriteAllText(blocked, "blocked");
        var controller = new SuiteController(store, ModuleCatalog.CreateModules());
        var errors = new List<string>();
        controller.ErrorReported += errors.Add;
        var settings = new SettingsHost(controller);
        using var tray = new TrayHost(controller, () => settings.Show(), () => Task.CompletedTask);
        try
        {
            await controller.StartAsync();
            Assert(StateOf(controller, "pinto") == ToolState.Failed, "启动失败必须显示失败状态");
            Assert(StateOf(controller, "clipboard") == ToolState.Running && StateOf(controller, "perf-monitor") == ToolState.Running,
                "单模块启动失败不得影响其他模块：" + Describe(controller));
            Assert(errors.Any(x => x.Contains("便签")), "启动失败必须给出含工具名的可操作提示：" + string.Join(" | ", errors));
            tray.RefreshMenuForVerification();
            var group = Group(tray, "pinto");
            Assert(HeaderText(group).Contains("启动失败"), "托盘分组应显示启动失败状态");
            Assert(HeaderText(Toggle(tray, "pinto")) == "重新启用", "失败分组应提供重新启用动作");
            log.Add("单模块启动失败显示失败状态与可操作提示，其他模块继续运行");

            tray.InvokeSettingsMenuForVerification();
            var failedWindow = settings.Current ?? throw new Exception("启动失败时统一设置不可用");
            Assert(failedWindow.RetryButtonForVerification("pinto") is { } retry && (retry.Content as string) == "重试启动",
                "设置应对启动失败的工具给出重试动作");

            File.Delete(blocked);
            ClickMenuItem(Toggle(tray, "pinto"));
            await WaitUntil(() => StateOf(controller, "pinto") == ToolState.Running, "失败模块重试启动");
            settings.Close();
            log.Add("清理故障后可从托盘与统一设置重试启动失败模块");
        }
        finally { settings.Close(); await controller.StopAsync(); }
    }

    private static void AssertThreeGroups(TrayHost tray)
    {
        var menu = tray.MenuForVerification;
        foreach (var id in ToolIds.All)
            Assert(Group(tray, id) is not null, $"托盘缺少工具分组：{id}");
        Assert(menu.Items.OfType<MenuItem>().Any(x => (string?)x.Tag == "settings")
                && menu.Items.OfType<MenuItem>().Any(x => (string?)x.Tag == "exit"), "托盘必须提供统一设置与退出");
        Assert(menu.Items.Count == ToolIds.All.Count + 3, "托盘应只有登记工具分组、分隔、设置与退出");
    }

    /// <summary>托盘浮层必须用套件自己的卡片模板。ContextMenu 是弹出层的根元素，WPF 不会把它
    /// Resources 里的隐式样式套到它自己身上；漏掉显式套用就会退回系统默认模板——方角、灰描边、
    /// 亮色分隔线，以及压住首列文字的浅色滚动条衬底（暗色下尤为明显）。</summary>
    private static void AssertTrayMenuSkin(TrayHost tray)
    {
        var menu = tray.MenuForVerification;
        menu.ApplyTemplate();
        var root = System.Windows.Media.VisualTreeHelper.GetChildrenCount(menu) > 0
            ? System.Windows.Media.VisualTreeHelper.GetChild(menu, 0) : null;
        Assert(root is System.Windows.Controls.Border { CornerRadius.TopLeft: > 0 },
            "托盘菜单必须落在套件卡片模板上（16 DIP 圆角），当前退回系统默认模板");
        var separator = menu.Items.OfType<System.Windows.Controls.Separator>().FirstOrDefault();
        Assert(separator is not null, "托盘菜单必须保留分隔线");
        separator!.ApplyTemplate();
        var bar = System.Windows.Media.VisualTreeHelper.GetChildrenCount(separator) > 0
            ? System.Windows.Media.VisualTreeHelper.GetChild(separator, 0) as System.Windows.Controls.Border : null;
        var divider = menu.TryFindResource("SuiteMenuDivider") as System.Windows.Media.Brush;
        Assert(bar?.Background is { } fill && divider is not null && Argb(fill) == Argb(divider),
            "菜单分隔线必须取 SuiteMenuDivider 发丝线，否则退回系统默认的浅色横线");
    }

    private static void AssertClipboardGroupHasNoSummon(TrayHost tray)
    {
        var labels = Group(tray, "clipboard").Items.OfType<MenuItem>().Select(HeaderText).ToList();
        Assert(labels.All(x => !x.Contains("呼出") && !x.Contains("面板")), "剪贴板分组不得出现面板呼出动作：" + string.Join("、", labels));
        Assert(labels.Any(x => x == "清空历史"), "剪贴板分组应保留清空历史动作");
        Assert(labels.Any(x => x.Contains("快捷键")), "剪贴板分组应保留快捷键管理动作");
    }

    private static MenuItem Group(TrayHost tray, string id) =>
        tray.MenuForVerification.Items.OfType<MenuItem>().SingleOrDefault(x => (string?)x.Tag == id)
        ?? throw new Exception("缺少托盘分组：" + id);

    private static MenuItem Toggle(TrayHost tray, string id) => (MenuItem)Group(tray, id).Items[0];

    /// <summary>菜单项标题可能是纯文本，也可能是"名称 + 次要状态"的双色 TextBlock。</summary>
    private static string HeaderText(MenuItem item) =>
        item.Header switch
        {
            string text => text,
            System.Windows.Controls.TextBlock block => string.Concat(block.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)),
            null => string.Empty,
            var other => other.ToString() ?? string.Empty,
        };

    private static void ClickMenuItem(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static int Argb(System.Windows.Media.Brush brush) => ColorOf(brush).ToArgb();

    private static System.Drawing.Color ColorOf(System.Windows.Media.Brush brush) =>
        brush is System.Windows.Media.SolidColorBrush solid
            ? System.Drawing.Color.FromArgb(solid.Color.A, solid.Color.R, solid.Color.G, solid.Color.B)
            : System.Drawing.Color.Transparent;

    private static void ClickToggle(UnifiedSettingsPage window, string id, bool enabled)
    {
        var toggle = window.ToolToggleForVerification(id) ?? throw new Exception("设置缺少工具开关：" + id);
        toggle.IsChecked = enabled;
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static ToolState StateOf(SuiteController controller, string id) =>
        controller.Tools.Single(x => x.Id == id).State;

    private static int HiddenNotes(SuiteController controller) =>
        controller.Tools.Single(x => x.Id == "pinto").Commands.Count(c => c.Id.StartsWith("show-note/"));

    private static string VisibilityLabel(SuiteController controller) =>
        controller.Tools.Single(x => x.Id == "perf-monitor").Commands.Single(c => c.Id == "visibility").Label;

    private static string PintoAccents(string directory)
    {
        var path = Path.Combine(directory, "state.json");
        if (!File.Exists(path)) return "";
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return string.Join(",", document.RootElement.GetProperty("notes").EnumerateArray()
            .Select(note => note.GetProperty("accent").ToString()));
    }

    private static int CountNotes(string directory)
    {
        var path = Path.Combine(directory, "state.json");
        if (!File.Exists(path)) return 0;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("notes").GetArrayLength();
    }

    private static string Describe(SuiteController controller) =>
        string.Join("，", controller.Tools.Select(x => $"{x.DisplayName}={x.State}"));

    private static async Task WaitUntil(Func<bool> condition, string what, int timeoutMs = 8000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMs) throw new Exception("等待超时：" + what);
            await Task.Delay(25);
        }
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
}
