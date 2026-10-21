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
        var themes = new ComboBox { Margin = new Thickness(0, 6, 0, 12) };
        foreach (var label in new[] { "跟随系统", "浅色", "深色" }) themes.Items.Add(label);
        themes.SelectedIndex = (int)controller.Settings.Theme;
        themes.SelectionChanged += (_, _) =>
        {
            try { controller.SetTheme((ThemePreference)themes.SelectedIndex); }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "主题保存失败"); themes.SelectedIndex = (int)controller.Settings.Theme; }
        };
        content.Children.Add(themes);
        content.Children.Add(new TextBlock { Text = "开机启动：关闭（安装与自启工单接入后可设置）", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "数据独立保存，不读取原工具的数据。", Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        controller.Changed += Refresh;
        Closed += (_, _) => controller.Changed -= Refresh;
        Refresh();
    }
    private void Refresh()
    {
        tools.Children.Clear();
        foreach (var tool in controller.Tools)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var enabled = new CheckBox { Content = tool.DisplayName, IsChecked = tool.Enabled, IsEnabled = tool.State != ToolState.Unavailable };
            enabled.Click += async (_, _) => await controller.SetEnabledAsync(tool.Id, enabled.IsChecked == true);
            row.Children.Add(enabled);
            row.Children.Add(new TextBlock { Text = StateLabel(tool), Margin = new Thickness(20, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            foreach (var command in tool.Commands.Where(c => !c.RequiresRunning))
            {
                var button = new Button { Content = command.Label, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(20, 4, 0, 0) };
                button.Click += async (_, _) => await controller.ExecuteCommandAsync(tool.Id, command.Id);
                row.Children.Add(button);
            }
            tools.Children.Add(row);
        }
    }
    internal static string StateLabel(ToolStatus tool) => tool.State switch
    {
        ToolState.Unavailable => "尚未接入，此版本不可用；启用偏好已保留。",
        ToolState.Running => tool.Error is null ? "运行中" : $"运行中；操作失败：{tool.Error}",
        ToolState.Disabled => "已停用",
        _ => $"启动或停止失败：{tool.Error}（可重新启用重试）"
    };
}
