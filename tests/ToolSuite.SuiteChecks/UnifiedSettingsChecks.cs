using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PerfMonitor.Windows.Settings;
using ToolSuite.App;
using ToolSuite.Core;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Point = System.Windows.Point;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace ToolSuite.SuiteChecks;

internal static class UnifiedSettingsChecks
{
    internal static async Task RunAsync(string root, List<string> log)
    {
        var store = new SuiteSettingsStore(root);
        var preferences = store.Load();
        foreach (var id in ToolIds.All) preferences.EnabledTools[id] = false;
        store.Save(preferences);
        var corruptSettings = Path.Combine(store.ModuleDirectory(ToolIds.PerfMonitor), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(corruptSettings)!);
        File.WriteAllText(corruptSettings, "{broken");
        var controller = new SuiteController(store, ModuleCatalog.CreateModules());
        using var theme = new SuiteTheme(controller, Application.Current.Dispatcher);
        using var host = new SettingsHost(controller);
        using var tray = new TrayHost(controller, () => host.Show(), () => Task.CompletedTask);
        try
        {
            await controller.StartAsync();
            tray.RefreshMenuForVerification();
            var reminderMenu = tray.MenuForVerification.Items.OfType<MenuItem>().Single(x => (string?)x.Tag == ToolIds.Reminders);
            var trayCreateReminder = reminderMenu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "新增提醒…"));
            Assert(trayCreateReminder.IsEnabled, "提醒停用时托盘新增入口仍可用");
            trayCreateReminder.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(30);
            var initialEditor = Application.Current.Windows.OfType<ToolSuite.Reminders.ReminderEditorWindow>().Single();
            Assert(host.Current is null && !Application.Current.Windows.OfType<PerfMonitor.App.SettingsWindow>().Any(),
                "托盘新增只打开独立窗口，不创建统一设置窗口");
            initialEditor.Close();
            log.Add("真实托盘新增提醒打开独立窗口，停用时可用且不创建设置窗口");
            tray.InvokeSettingsMenuForVerification();
            var window = host.Current ?? throw new Exception("统一设置入口没有显示窗口");
            Assert(Math.Abs(window.View.ActualWidth - 380) < 1, "统一设置必须保持性能条早期设置页的 380 DIP 宽度");
            Assert(Application.Current.Windows.OfType<PerfMonitor.App.SettingsWindow>().SingleOrDefault() == window.View,
                "统一设置必须复用性能条现有设置窗口，且只创建一个窗口");
            var sections = Descendants(window.View).OfType<Expander>().ToArray();
            Assert(sections.Length == 4, "应按剪贴板、性能条、便签、循环提醒显示四个可折叠分组");
            Assert(sections.All(x => !x.IsExpanded), "首次打开工具分组默认收起");
            var fixedHeight = window.View.ActualHeight;
            var settingsScroll = (ScrollViewer)window.View.FindName("SettingsScroll");
            var fixedViewportWidth = settingsScroll.ViewportWidth;
            var fixedContentWidth = ((FrameworkElement)settingsScroll.Content).ActualWidth;
            foreach (var section in sections) section.IsExpanded = true;
            window.View.UpdateLayout();
            Assert(Math.Abs(window.View.ActualHeight - fixedHeight) < 1,
                $"展开工具分组不得改变设置窗口高度：{fixedHeight} → {window.View.ActualHeight}");
            Assert(settingsScroll.ScrollableHeight > 0, "超出固定高度的设置应在内部滚动");
            Assert(Math.Abs(settingsScroll.ViewportWidth - fixedViewportWidth) < 1
                && Math.Abs(((FrameworkElement)settingsScroll.Content).ActualWidth - fixedContentWidth) < 1,
                $"滚动条出现不得改变设置内容宽度：视口 {fixedViewportWidth} → {settingsScroll.ViewportWidth}，内容 {fixedContentWidth} → {((FrameworkElement)settingsScroll.Content).ActualWidth}");
            Assert(Math.Abs(window.View.ActualWidth - 380) < 1, "滚动条出现不得加宽设置窗口");
            var verticalBar = (ScrollBar)settingsScroll.Template.FindName("PART_VerticalScrollBar", settingsScroll);
            var contentRight = ((FrameworkElement)settingsScroll.Content).TransformToAncestor(settingsScroll)
                .Transform(new Point(fixedContentWidth, 0)).X;
            var barLeft = verticalBar.TransformToAncestor(settingsScroll).Transform(new Point()).X;
            Assert(verticalBar.IsVisible && barLeft >= contentRight,
                $"滚动条应位于右侧留白，不能遮挡设置内容：可见 {verticalBar.IsVisible}，内容右侧 {contentRight}，滚动条左侧 {barLeft}，宽度 {verticalBar.ActualWidth}");
            var scrollThumb = Descendants(verticalBar).OfType<Thumb>().Single();
            scrollThumb.RaiseEvent(new DragDeltaEventArgs(0, 40) { RoutedEvent = Thumb.DragDeltaEvent });
            await Task.Delay(30);
            Assert(settingsScroll.VerticalOffset > 0, "滚动条仍应能滚动设置内容");
            settingsScroll.ScrollToTop();
            await Task.Delay(30);
            foreach (var section in sections) section.IsExpanded = false;
            window.View.UpdateLayout();
            Assert(Math.Abs(window.View.ActualHeight - fixedHeight) < 1, "收起工具分组不得改变设置窗口高度");
            Assert(Math.Abs(settingsScroll.ViewportWidth - fixedViewportWidth) < 1
                && Math.Abs(((FrameworkElement)settingsScroll.Content).ActualWidth - fixedContentWidth) < 1,
                "滚动条隐藏后应保持相同的内容宽度");
            log.Add("滚动条显示/隐藏不改变窗口或内容宽度，位于右侧留白且可正常滚动");
            Assert(((TextBlock)window.View.FindName("SaveStatusText")).Text.Contains("配置损坏"),
                "合并页面必须保留原性能条设置的损坏配置恢复提示");
            Assert(controller.Tools.All(x => x.State == ToolState.Disabled), "打开设置不得启动停用工具");
            log.Add("真实托盘打开复用性能条的统一设置窗口；四个工具首次收起，停用状态不变");

            var clipboard = sections.Single(x => AutomationProperties.GetAutomationId(x) == "clipboard-settings");
            var performance = sections.Single(x => AutomationProperties.GetAutomationId(x) == "perf-monitor-settings");
            var notes = sections.Single(x => AutomationProperties.GetAutomationId(x) == "pinto-settings");
            var peer = new ExpanderAutomationPeer(performance);
            var disclosure = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
            disclosure.Expand();
            notes.IsExpanded = true;
            Assert(performance.IsExpanded && notes.IsExpanded && !clipboard.IsExpanded, "分组必须独立展开");
            disclosure.Collapse();
            Assert(!performance.IsExpanded && notes.IsExpanded, "收起一个分组不得收起其他分组");
            host.Close();
            var reopened = host.Show();
            Assert(!ReferenceEquals(window.View, reopened.View), "关闭后应释放并重建设置窗口");
            Assert(store.Load().ExpandedSettingsSections.GetValueOrDefault(ToolIds.Pinto), "展开偏好应落盘");
            sections = Descendants(reopened.View).OfType<Expander>().ToArray();
            Assert(sections.Single(x => AutomationProperties.GetAutomationId(x) == "pinto-settings").IsExpanded,
                "重建窗口应恢复便签展开状态");
            log.Add("辅助功能展开/收起与独立分组可用；关闭重建恢复已保存的展开状态");

            Assert(await controller.ExecuteCommandAsync(ToolIds.Clipboard, "change-shortcut"), "剪贴板设置入口不可用");
            Assert(ReferenceEquals(reopened, host.Current), "剪贴板入口必须复用同一设置窗口");
            Assert(sections.Single(x => AutomationProperties.GetAutomationId(x) == "clipboard-settings").IsExpanded,
                "剪贴板入口应自动展开对应分组");
            Assert(await controller.ExecuteCommandAsync(ToolIds.PerfMonitor, "settings"), "性能条设置入口不可用");
            Assert(ReferenceEquals(reopened, host.Current)
                && Application.Current.Windows.OfType<PerfMonitor.App.SettingsWindow>().Count() == 1,
                "模块入口不得另开独立设置窗口");
            log.Add("剪贴板和性能条入口复用同一窗口并定位各自分类");

            Assert(await controller.ExecuteCommandAsync(ToolIds.Reminders, "settings"), "循环提醒管理入口不可用");
            Assert(ReferenceEquals(reopened, host.Current)
                && sections.Single(x => AutomationProperties.GetAutomationId(x) == "reminders-settings").IsExpanded,
                "提醒入口应复用统一设置并展开循环提醒分类");
            reopened.View.UpdateLayout();
            var reminderControl = Descendants(reopened.View).OfType<ToolSuite.Reminders.ReminderSettingsControl>().Single();
            var newReminder = Descendants(reminderControl).OfType<System.Windows.Controls.Button>().Single(b => Equals(b.Content, "新增提醒…"));
            newReminder.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            reopened.View.UpdateLayout();
            var reminderEditor = Application.Current.Windows.OfType<ToolSuite.Reminders.ReminderEditorWindow>().Single();
            reminderEditor.UpdateLayout();
            Assert(!Descendants(reminderControl).OfType<TextBox>().Any(), "设置只显示列表，不包含新增表单");
            var reminderName = Descendants(reminderEditor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "提醒名称");
            reminderName.Text = "宿主离线提醒";
            Descendants(reminderEditor).OfType<System.Windows.Controls.Button>().Single(b => Equals(b.Content, "创建提醒"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var reminderModule = controller.GetModule<ToolSuite.Reminders.ReminderModule>(ToolIds.Reminders)!;
            Assert(reminderModule.Schedule.Reminders.Single().Name == "宿主离线提醒" && !reminderModule.Schedule.IsRunning,
                "独立窗口离线新建应保存配置，不隐式启动");
            Assert(await controller.SetEnabledAsync(ToolIds.Reminders, true), "提醒工具应能独立启用");
            Assert(reminderModule.Schedule.IsRunning && reminderModule.Schedule.Remaining(reminderModule.Schedule.Reminders.Single().Id) is not null,
                "启用提醒工具后开始计时");
            Assert(await controller.SetEnabledAsync(ToolIds.Reminders, false), "提醒工具应能独立停用");
            log.Add("管理入口定位第四分类，新增与编辑使用独立窗口，离线保存和独立启停正确");

            var gpu = (ToggleButton)reopened.View.FindName("GpuToggle");
            var gpuBefore = gpu.IsChecked;
            gpu.IsChecked = gpuBefore != true;
            var centered = (ToggleButton)reopened.View.FindName("CenterInTaskbarRowToggle");
            centered.IsChecked = true;
            var transparent = (ToggleButton)reopened.View.FindName("TransparentDisplayToggle");
            transparent.IsChecked = true;
            var performancePath = Path.Combine(store.ModuleDirectory(ToolIds.PerfMonitor), "settings.json");
            var saved = new WindowsSettingsStore(performancePath).Load();
            Assert(saved.Metrics.Gpu == (gpuBefore != true) && saved.CenterInTaskbarRow && saved.TransparentDisplay,
                "离线修改指标、居中和透明显示必须保存");
            Assert(controller.Tools.All(x => x.State == ToolState.Disabled), "编辑离线设置不得启动任何工具");
            using (var locked = new FileStream(performancePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                gpu.IsChecked = gpuBefore;
                Assert(gpu.IsChecked == (gpuBefore != true), "保存失败必须还原控件值");
                Assert(((TextBlock)reopened.View.FindName("SaveStatusText")).Text.Contains("保存失败"),
                    "保存失败必须在页面显示原因");
            }
            gpu.IsChecked = gpuBefore;
            gpu.IsChecked = gpuBefore != true;
            log.Add("停用时可保存性能条指标、居中和透明显示；写入失败恢复原值并显示原因");

            reopened.View.UpdateLayout();
            var input = Descendants(reopened.View).OfType<TextBox>()
                .Single(x => AutomationProperties.GetName(x) == "剪贴板呼出快捷键");
            var oldShortcut = input.Text;
            input.Text = "Control+Alt+F11";
            input.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, input, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert(new ClipboardTool.Infrastructure.Windows.JsonStore(store.ModuleDirectory(ToolIds.Clipboard)).ReadSettings().Shortcut == "Control+Alt+F11",
                "离线快捷键编辑未保存");
            input.Text = "无效快捷键";
            input.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, input, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert(new ClipboardTool.Infrastructure.Windows.JsonStore(store.ModuleDirectory(ToolIds.Clipboard)).ReadSettings().Shortcut == "Control+Alt+F11",
                "无效快捷键不得覆盖已保存值");
            input.Text = oldShortcut;
            input.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, input, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            log.Add("剪贴板离线快捷键保存有效，无效输入保留原配置");

            foreach (var preference in new[] { ThemePreference.Light, ThemePreference.Dark })
            {
                controller.SetTheme(preference);
                await Task.Delay(100);
                foreach (var section in sections) section.IsExpanded = false;
                reopened.View.UpdateLayout();
                ((ScrollViewer)reopened.View.FindName("SettingsScroll")).ScrollToTop();
                reopened.View.UpdateLayout();
                var background = (SolidColorBrush)reopened.View.Background;
                var expected = preference == ThemePreference.Dark ? Color.FromRgb(11, 11, 14) : Color.FromRgb(245, 245, 247);
                Assert(background.Color == expected, "统一设置未跟随套件亮暗主题");
                foreach (var width in new[] { 380d })
                {
                    reopened.View.Width = width;
                    reopened.View.UpdateLayout();
                    Assert(reopened.View.ActualWidth >= 380 && reopened.View.ActualHeight <= reopened.View.MaxHeight + 1,
                        "设置窗口应适应紧凑宽度与显示器高度");
                    var bitmap = new RenderTargetBitmap((int)reopened.View.ActualWidth, (int)reopened.View.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(reopened.View);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var image = File.Create(Path.Combine(root, $"settings-{preference}-{width}.png"));
                    encoder.Save(image);
                }
            }
            sections.Single(x => AutomationProperties.GetAutomationId(x) == "perf-monitor-settings").IsExpanded = true;
            reopened.View.UpdateLayout();
            var header = (ToggleButton)sections[0].Template.FindName("HeaderSite", sections[0]);
            header.BringIntoView();
            Assert(header.Focusable && header.FocusVisualStyle is not null && header.Focus(), "分组标题必须支持键盘焦点与可见焦点提示");
            log.Add("亮暗主题、恢复 380 DIP 固定宽度与键盘分组焦点通过，已生成视觉预览");
            Assert(await controller.SetEnabledAsync(ToolIds.PerfMonitor, true), "无法启用性能条进行运行编辑检查");
            var font = (Slider)reopened.View.FindName("FontSizeSlider");
            var nextFontSize = font.Value == 14 ? 16 : 14;
            font.Value = nextFontSize;
            Assert(new WindowsSettingsStore(performancePath).Load().FontSize == nextFontSize,
                "性能条运行时修改字号必须保存，不能触发模块自启校验");
            var bar = Application.Current.Windows.OfType<PerfMonitor.App.PerformanceBarWindow>().Single();
            Assert(((PerfMonitor.App.PerformanceBarViewModel)bar.DataContext).FontSize == nextFontSize,
                "性能条运行时修改字号应立即更新实际窗口");
            log.Add("运行中的性能条配置可保存并即时更新窗口，不写入模块自启设置");
            sections.Single(x => AutomationProperties.GetAutomationId(x) == "perf-monitor-settings").IsExpanded = false;
            bar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent });
            await Task.Delay(60);
            var contextMenu = PresentationSource.CurrentSources.Cast<PresentationSource>()
                .Where(source => source.RootVisual is not null)
                .SelectMany(source => new DependencyObject[] { source.RootVisual }.Concat(Descendants(source.RootVisual)))
                .OfType<ContextMenu>().Distinct()
                .Single(menu => menu.IsOpen && menu.Items.OfType<MenuItem>().Any(item => (string?)item.Header == "打开设置"));
            contextMenu.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "打开设置")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            contextMenu.IsOpen = false;
            Assert(ReferenceEquals(reopened, host.Current)
                && sections.Single(x => AutomationProperties.GetAutomationId(x) == "perf-monitor-settings").IsExpanded,
                "性能条实际右键菜单应定位同一个统一设置窗口");
            log.Add("性能条实际右键菜单的打开设置命令复用统一窗口并展开性能条分类");
        }
        finally
        {
            host.Close();
            await controller.StopAsync();
        }
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
