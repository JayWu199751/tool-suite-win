using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ToolSuite.Core;
using ClipboardTool.Presentation.Wpf;
using PerfMonitor.App;
using PerfMonitor.Core.Settings;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace ToolSuite.App;

/// <summary>在性能条原设置窗口中组合通用偏好及三个工具；模块仍负责自己的配置事务。</summary>
internal sealed class UnifiedSettingsPage
{
    private readonly SuiteController controller;
    private readonly Dictionary<string, Expander> sections = [];
    private readonly Dictionary<string, CheckBox> toggles = [];
    private readonly Dictionary<string, TextBlock> states = [];
    private readonly Dictionary<string, Button> retries = [];
    private readonly Dictionary<string, Button> noteActions = [];
    private readonly ComboBox themes = new() { Width = 130 };
    private readonly CheckBox startWithWindows = new() { Content = "开机启动" };
    private readonly TextBlock autoStartStatus = new();
    private readonly TextBlock feedback = new();
    private readonly TextBox shortcut = new() { MinHeight = 32, Padding = new Thickness(12, 8, 12, 8) };
    private readonly TextBlock shortcutStatus = new();
    private bool refreshing;
    private bool changingSection;

    internal PerfMonitor.App.SettingsWindow View { get; }
    internal bool IsVisible => View.IsVisible;

    public UnifiedSettingsPage(SuiteController controller)
    {
        this.controller = controller;
        View = new PerfMonitor.App.SettingsWindow(ReadPerformance(controller), patch => UpdatePerformance(controller, patch),
            controller.GetModule<PerfMonitorModule>(ToolIds.PerfMonitor)?.RecoveredInvalidSettings == true, listenForSystemTheme: false);
        View.Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/ToolSuite;component/Assets/app-icon-256.png", UriKind.Absolute));
        var performance = View.DetachSettingsContent();
        performance.IsEnabled = controller.GetModule<PerfMonitorModule>(ToolIds.PerfMonitor) is not null;
        var stack = new StackPanel { Margin = new Thickness(20, 0, 20, 16) };
        stack.Children.Add(BuildGeneral());
        foreach (var tool in controller.Tools)
        {
            var body = new StackPanel();
            body.Children.Add(BuildToolStatus(tool));
            if (tool.Id == ToolIds.Clipboard) body.Children.Add(BuildClipboard());
            if (tool.Id == ToolIds.PerfMonitor) body.Children.Add(performance);
            if (tool.Id == ToolIds.Pinto) body.Children.Add(BuildNotes());
            var heading = new StackPanel();
            var name = Text(tool.DisplayName, "SuiteInk", 15);
            name.FontWeight = FontWeights.SemiBold;
            heading.Children.Add(name);
            var state = Text("", "SuiteInkSecondary", 12);
            heading.Children.Add(state);
            states[tool.Id] = state;
            var section = new Expander { Header = heading, Content = body,
                IsExpanded = controller.Settings.ExpandedSettingsSections.GetValueOrDefault(tool.Id) };
            section.SetResourceReference(FrameworkElement.StyleProperty, "Suite.SettingsSection");
            AutomationProperties.SetName(section, tool.DisplayName + "设置");
            AutomationProperties.SetAutomationId(section, tool.Id + "-settings");
            section.Expanded += (_, _) => SaveSection(tool.Id, section);
            section.Collapsed += (_, _) => SaveSection(tool.Id, section);
            sections[tool.Id] = section;
            stack.Children.Add(Card(section));
        }
        feedback.TextWrapping = TextWrapping.Wrap;
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "SuiteDanger");
        feedback.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        stack.Children.Add(feedback);
        View.UseSuiteContent(stack);
        themes.SelectionChanged += (_, _) =>
        {
            if (refreshing || themes.SelectedIndex < 0) return;
            try { controller.SetTheme((ThemePreference)themes.SelectedIndex); feedback.Text = ""; }
            catch (Exception error) { feedback.Text = "主题保存失败：" + error.Message; Refresh(); }
        };
        startWithWindows.Click += (_, _) => controller.SetStartWithWindows(startWithWindows.IsChecked == true);
        controller.Changed += Refresh;
        controller.AppearanceChanged += Refresh;
        var performanceModule = controller.GetModule<PerfMonitorModule>(ToolIds.PerfMonitor);
        if (performanceModule is not null) performanceModule.SettingsChanged += Refresh;
        View.Activated += OnActivated;
        View.Closed += (_, _) =>
        {
            controller.Changed -= Refresh;
            controller.AppearanceChanged -= Refresh;
            if (performanceModule is not null) performanceModule.SettingsChanged -= Refresh;
            View.Activated -= OnActivated;
        };
        View.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; View.Close(); }
        };
        Refresh();
    }

    private static PerformanceSettings ReadPerformance(SuiteController controller)
        => controller.GetModule<PerfMonitorModule>(ToolIds.PerfMonitor)?.ReadSettings() ?? PerformanceSettings.Default;

    private static PerformanceSettings UpdatePerformance(SuiteController controller, SettingsPatch patch)
        => (controller.GetModule<PerfMonitorModule>(ToolIds.PerfMonitor)
            ?? throw new InvalidOperationException("性能条不可用。请重新启动工具套件。" )).UpdateSettings(patch);

    private void OnActivated(object? sender, EventArgs e) => Refresh();

    private UIElement BuildGeneral()
    {
        var stack = new StackPanel();
        AutomationProperties.SetAutomationId(stack, "general-settings");
        var title = Text("通用设置", "SuiteInkSecondary", 12);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(title);
        foreach (var label in new[] { "跟随系统", "浅色", "深色" }) themes.Items.Add(label);
        themes.SetResourceReference(FrameworkElement.StyleProperty, "SettingsComboStyle");
        AutomationProperties.SetName(themes, "应用主题");
        stack.Children.Add(Row("主题", themes));
        startWithWindows.SetResourceReference(FrameworkElement.StyleProperty, "Suite.SwitchRow");
        AutomationProperties.SetName(startWithWindows, "开机启动工具套件");
        stack.Children.Add(startWithWindows);
        autoStartStatus.TextWrapping = TextWrapping.Wrap;
        autoStartStatus.FontSize = 12;
        autoStartStatus.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(autoStartStatus);
        return Card(stack);
    }

    private UIElement BuildToolStatus(ToolStatus tool)
    {
        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var enabled = new CheckBox { Content = "启用" + tool.DisplayName };
        enabled.SetResourceReference(FrameworkElement.StyleProperty, "Suite.SwitchRow");
        AutomationProperties.SetName(enabled, "启用" + tool.DisplayName);
        enabled.Click += async (_, _) => await controller.SetEnabledAsync(tool.Id, enabled.IsChecked == true);
        toggles[tool.Id] = enabled;
        body.Children.Add(enabled);
        var retry = ActionButton("重试启动");
        retry.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        retry.Click += async (_, _) => await controller.SetEnabledAsync(tool.Id, true);
        retries[tool.Id] = retry;
        body.Children.Add(retry);
        return body;
    }

    private UIElement BuildClipboard()
    {
        var body = new StackPanel();
        body.Children.Add(Text("呼出快捷键", "SuiteInk", 13));
        shortcut.Margin = new Thickness(0, 8, 0, 8);
        shortcut.SetResourceReference(FrameworkElement.StyleProperty, "Suite.SettingsInput");
        shortcut.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "SuiteWell");
        shortcut.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SuiteInk");
        shortcut.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "SuiteStrong");
        shortcut.SetResourceReference(System.Windows.Controls.Control.FocusVisualStyleProperty, "Suite.FocusVisual");
        AutomationProperties.SetName(shortcut, "剪贴板呼出快捷键");
        shortcut.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SaveShortcut(); e.Handled = true; } };
        shortcut.LostKeyboardFocus += (_, _) => SaveShortcut();
        body.Children.Add(shortcut);
        body.Children.Add(Text("例如 Control+Shift+V，按 Enter 或移开焦点保存。", "SuiteInkSecondary", 12));
        shortcutStatus.FontSize = 12;
        shortcutStatus.TextWrapping = TextWrapping.Wrap;
        shortcutStatus.Margin = new Thickness(0, 8, 0, 0);
        shortcutStatus.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        body.Children.Add(shortcutStatus);
        return body;
    }

    private void SaveShortcut()
    {
        var module = controller.GetModule<ClipboardModule>(ToolIds.Clipboard);
        if (refreshing || module is null) return;
        try
        {
            if (shortcut.Text == module.ReadShortcut()) return;
            module.SetShortcut(shortcut.Text);
            shortcut.Text = module.ReadShortcut();
            shortcutStatus.Text = "已保存";
            shortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, "SuiteSuccess");
        }
        catch (Exception error)
        {
            shortcutStatus.Text = "未保存：" + error.Message;
            shortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, "SuiteDanger");
        }
    }

    private UIElement BuildNotes()
    {
        var body = new StackPanel();
        body.Children.Add(Text("每张便签的颜色和贴边可在便签上调整。", "SuiteInkSecondary", 12));
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (id, label) in new[] { ("export", "导出便签备份…"), ("import", "导入便签备份…"), ("recover", "从备份恢复损坏数据…") })
        {
            var button = ActionButton(label);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Click += async (_, _) => await controller.ExecuteCommandAsync(ToolIds.Pinto, id);
            noteActions[id] = button;
            actions.Children.Add(button);
        }
        body.Children.Add(actions);
        return body;
    }

    private void Refresh()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            foreach (var tool in controller.Tools)
            {
                toggles[tool.Id].IsChecked = tool.Enabled;
                toggles[tool.Id].IsEnabled = tool.State != ToolState.Unavailable;
                states[tool.Id].Text = StateLabel(tool);
                states[tool.Id].SetResourceReference(TextBlock.ForegroundProperty,
                    tool.State == ToolState.Failed ? "SuiteDanger" : "SuiteInkSecondary");
                retries[tool.Id].Visibility = tool.State == ToolState.Failed ? Visibility.Visible : Visibility.Collapsed;
                if (tool.Id == ToolIds.Pinto)
                    foreach (var (id, button) in noteActions)
                    {
                        var command = tool.Commands.FirstOrDefault(c => c.Id == id);
                        button.Visibility = id == "recover" && command is null ? Visibility.Collapsed : Visibility.Visible;
                        button.IsEnabled = command is not null && (!command.RequiresRunning || tool.State == ToolState.Running);
                    }
            }
            themes.SelectedIndex = (int)controller.Settings.Theme;
            startWithWindows.IsChecked = controller.Settings.StartWithWindows;
            autoStartStatus.Text = controller.AutoStartError is { } error ? "开机启动未生效：" + error
                : controller.Settings.StartWithWindows ? "登录后启动已启用的工具，并恢复窗口状态。" : "登录后不会自动运行工具套件。";
            autoStartStatus.SetResourceReference(TextBlock.ForegroundProperty,
                controller.AutoStartError is null ? "SuiteInkSecondary" : "SuiteDanger");
            var clipboard = controller.GetModule<ClipboardModule>(ToolIds.Clipboard);
            shortcut.IsEnabled = clipboard is not null;
            if (!shortcut.IsKeyboardFocusWithin && clipboard is not null) shortcut.Text = clipboard.ReadShortcut();
            View.ApplySettings(ReadPerformance(controller),
                controller.GetModule<PerfMonitorModule>(ToolIds.PerfMonitor)?.RecoveredInvalidSettings == true);
        }
        catch (Exception error) { feedback.Text = "无法读取设置：" + error.Message; }
        finally { refreshing = false; }
    }

    private void SaveSection(string id, Expander section)
    {
        if (changingSection) return;
        try { controller.SetSettingsSectionExpanded(id, section.IsExpanded); feedback.Text = ""; }
        catch (Exception error)
        {
            changingSection = true;
            section.IsExpanded = controller.Settings.ExpandedSettingsSections.GetValueOrDefault(id);
            changingSection = false;
            feedback.Text = "展开状态未保存：" + error.Message;
        }
    }

    internal void OpenSection(string id)
    {
        if (!sections.TryGetValue(id, out var section)) return;
        section.IsExpanded = true;
        View.Dispatcher.BeginInvoke(new Action(() => { if (View.IsVisible) section.BringIntoView(); }));
    }

    private static TextBlock Text(string value, string brush, double size)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return text;
    }

    private static Border Card(UIElement body)
    {
        var card = new Border { Child = body };
        card.SetResourceReference(FrameworkElement.StyleProperty, "Suite.Card");
        return card;
    }

    private static Grid Row(string label, UIElement value)
    {
        var row = new Grid { MinHeight = 44, Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Text(label, "SuiteInk", 13);
        text.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        row.Children.Add(text);
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }

    private static Button ActionButton(string label)
    {
        var button = new Button { Content = label };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Suite.GhostButton");
        return button;
    }

    internal CheckBox? ToolToggleForVerification(string id) => toggles.GetValueOrDefault(id);
    internal Button? RetryButtonForVerification(string id) => retries.GetValueOrDefault(id);
    internal ComboBox ThemeSelectorForVerification => themes;
    internal CheckBox StartWithWindowsToggleForVerification => startWithWindows;
    internal TextBlock AutoStartStatusForVerification => autoStartStatus;
    internal static string StateLabel(ToolStatus tool) => ToolStateText.Detail(tool);
}
