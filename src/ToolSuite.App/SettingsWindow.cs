using System.Windows;
using System.Windows.Controls;
using ToolSuite.Core;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;

namespace ToolSuite.App;

internal sealed class SettingsWindow : Window
{
    private readonly SuiteController controller;
    private readonly StackPanel tools = new() { Margin = new Thickness(0, 20, 0, 16) };
    private readonly ComboBox themes = new() { Margin = new Thickness(0, 6, 0, 12) };
    private readonly Dictionary<string, CheckBox> toggles = new();
    private readonly Dictionary<string, Button> retries = new();
    private readonly CheckBox startWithWindows = new()
    {
        Content = "开机启动（登录后运行已启用的工具）", Margin = new Thickness(0, 4, 0, 4)
    };
    private readonly TextBlock autoStartStatus = new()
    {
        TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 8)
    };
    public SettingsWindow(SuiteController controller)
    {
        this.controller = controller;
        Title = "工具套件设置"; Width = 460; Height = 440; MinWidth = 400; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SuiteBackground");
        SetResourceReference(ForegroundProperty, "SuiteForeground");
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "工具套件", FontSize = 24 });
        content.Children.Add(tools);
        content.Children.Add(new TextBlock { Text = "主题" });
        foreach (var label in new[] { "跟随系统", "浅色", "深色" }) themes.Items.Add(label);
        themes.SelectedIndex = (int)controller.Settings.Theme;
        themes.SelectionChanged += (_, _) =>
        {
            try { controller.SetTheme((ThemePreference)themes.SelectedIndex); }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "主题保存失败"); themes.SelectedIndex = (int)controller.Settings.Theme; }
        };
        content.Children.Add(themes);
        // 失败只把开关恢复并显示原因，不把失败当成已生效。
        startWithWindows.Click += (_, _) => controller.SetStartWithWindows(startWithWindows.IsChecked == true);
        content.Children.Add(startWithWindows);
        content.Children.Add(autoStartStatus);
        content.Children.Add(new TextBlock { Text = "数据独立保存，不读取原工具的数据。", Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        controller.Changed += Refresh;
        Closed += (_, _) => controller.Changed -= Refresh;
        Refresh();
    }
    private void Refresh()
    {
        tools.Children.Clear();
        toggles.Clear();
        retries.Clear();
        foreach (var tool in controller.Tools)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var enabled = new CheckBox { Content = tool.DisplayName, IsChecked = tool.Enabled, IsEnabled = tool.State != ToolState.Unavailable };
            enabled.Click += async (_, _) => await controller.SetEnabledAsync(tool.Id, enabled.IsChecked == true);
            toggles[tool.Id] = enabled;
            row.Children.Add(enabled);
            row.Children.Add(new TextBlock { Text = StateLabel(tool), Margin = new Thickness(20, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            if (tool.State == ToolState.Failed)
            {
                // 失败不伪装运行：给出可操作提示，并可原地重试启用（重试前由控制器清理残留）。
                var retry = new Button { Content = "重试启动", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(20, 4, 0, 0) };
                retry.Click += async (_, _) => await controller.SetEnabledAsync(tool.Id, true);
                retries[tool.Id] = retry;
                row.Children.Add(retry);
            }
            foreach (var command in tool.Commands.Where(c => !c.RequiresRunning))
            {
                var button = new Button { Content = command.Label, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(20, 4, 0, 0) };
                button.Click += async (_, _) => await controller.ExecuteCommandAsync(tool.Id, command.Id);
                row.Children.Add(button);
            }
            tools.Children.Add(row);
        }
        themes.SelectedIndex = (int)controller.Settings.Theme;
        startWithWindows.IsChecked = controller.Settings.StartWithWindows;
        autoStartStatus.Text = controller.AutoStartError is { } error
            ? "开机启动未生效：" + error
            : controller.Settings.StartWithWindows
                ? "登录后只启动已启用的工具，窗口按套件保存的状态恢复。"
                : "登录后不会自动运行工具套件。";
    }
    internal CheckBox? ToolToggleForVerification(string id) => toggles.GetValueOrDefault(id);
    internal Button? RetryButtonForVerification(string id) => retries.GetValueOrDefault(id);
    internal ComboBox ThemeSelectorForVerification => themes;
    internal CheckBox StartWithWindowsToggleForVerification => startWithWindows;
    internal TextBlock AutoStartStatusForVerification => autoStartStatus;
    internal static string StateLabel(ToolStatus tool) => ToolStateText.Detail(tool);
}
