using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using ToolSuite.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;

namespace ToolSuite.App;

/// <summary>宿主统一设置：三个工具的行为卡片 + 外观 + 启动。
/// 视觉语言见 docs/design-system.md，控件样式来自 Themes/HostControls.xaml（应用级键控样式）。</summary>
internal sealed class SettingsWindow : Window
{
    private readonly SuiteController controller;
    private readonly StackPanel tools = new();
    private readonly ComboBox themes = new() { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
    private readonly Dictionary<string, CheckBox> toggles = new();
    private readonly Dictionary<string, Button> retries = new();
    private readonly CheckBox startWithWindows = new() { Content = "开机启动" };
    private readonly TextBlock autoStartStatus = new() { Margin = new Thickness(0, 8, 0, 2) };

    public SettingsWindow(SuiteController controller)
    {
        this.controller = controller;
        ConfigureWindow();
        Content = BuildContent();

        themes.SelectedIndex = (int)controller.Settings.Theme;
        themes.SelectionChanged += (_, _) =>
        {
            try { controller.SetTheme((ThemePreference)themes.SelectedIndex); }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "主题保存失败"); themes.SelectedIndex = (int)controller.Settings.Theme; }
        };
        // 失败只把开关恢复并显示原因，不把失败当成已生效。
        startWithWindows.Click += (_, _) => controller.SetStartWithWindows(startWithWindows.IsChecked == true);

        controller.Changed += Refresh;
        Closed += (_, _) => controller.Changed -= Refresh;
        Refresh();
    }

    private void ConfigureWindow()
    {
        Title = "工具套件设置";
        Width = 480;
        MinWidth = 480;
        MaxWidth = 480;
        MinHeight = 320;
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 80);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.SingleBorderWindow;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "SuiteBackground");
        SetResourceReference(ForegroundProperty, "SuiteInk");
        // 压掉原生标题栏，边框同时充当 1 DIP 外环；关闭与拖动由内容区自绘。
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(20),
            GlassFrameThickness = new Thickness(0)
        });
    }

    private UIElement BuildContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildHeader());

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        var stack = new StackPanel { Margin = new Thickness(20, 0, 20, 16) };
        scroll.Content = stack;
        root.Children.Add(scroll);

        stack.Children.Add(BuildCard("工具", tools));
        stack.Children.Add(BuildAppearanceCard());
        stack.Children.Add(BuildStartupCard());
        var footnote = new TextBlock
        {
            Text = "数据独立保存，不读取原工具的数据。",
            Margin = new Thickness(4, 2, 0, 0)
        };
        footnote.SetResourceReference(TextBlock.StyleProperty, "Suite.Caption");
        stack.Children.Add(footnote);
        return root;
    }

    private UIElement BuildHeader()
    {
        var header = new Grid
        {
            Background = Brushes.Transparent,
            MinHeight = 76,
            Margin = new Thickness(24, 20, 24, 8)
        };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = "工具套件", FontSize = 21, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "SuiteInk");
        var subtitle = new TextBlock { Text = "三个工具的开关、状态与外观", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "SuiteInkSecondary");
        titles.Children.Add(title);
        titles.Children.Add(subtitle);
        header.Children.Add(titles);

        var close = new Button
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        close.SetResourceReference(StyleProperty, "Suite.CloseButton");
        close.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "关闭设置窗口");
        close.Click += (_, _) => Close();
        header.Children.Add(close);
        return header;
    }

    private static Border BuildCard(string title, UIElement body)
    {
        var container = new StackPanel();
        container.Children.Add(GroupTitle(title));
        container.Children.Add(body);
        var card = new Border();
        card.SetResourceReference(StyleProperty, "Suite.Card");
        card.Child = container;
        return card;
    }

    private Border BuildAppearanceCard()
    {
        var row = new Grid();
        row.SetResourceReference(StyleProperty, "Suite.Row");
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Label("主题"));
        foreach (var label in new[] { "跟随系统", "浅色", "深色" }) themes.Items.Add(label);
        themes.Width = 130;
        themes.SetResourceReference(StyleProperty, "Suite.Combo");
        Grid.SetColumn(themes, 1);
        row.Children.Add(themes);

        var card = new Border();
        card.SetResourceReference(StyleProperty, "Suite.Card");
        var stack = new StackPanel();
        stack.Children.Add(GroupTitle("外观"));
        stack.Children.Add(row);
        card.Child = stack;
        return card;
    }

    private Border BuildStartupCard()
    {
        startWithWindows.SetResourceReference(StyleProperty, "Suite.SwitchRow");
        autoStartStatus.SetResourceReference(TextBlock.StyleProperty, "Suite.Caption");
        autoStartStatus.Margin = new Thickness(0, 4, 0, 2);
        var stack = new StackPanel();
        stack.Children.Add(GroupTitle("启动"));
        stack.Children.Add(startWithWindows);
        stack.Children.Add(autoStartStatus);
        var card = new Border();
        card.SetResourceReference(StyleProperty, "Suite.Card");
        card.Child = stack;
        return card;
    }

    private static TextBlock GroupTitle(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(TextBlock.StyleProperty, "Suite.GroupTitle");
        return block;
    }

    private static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(TextBlock.StyleProperty, "Suite.Label");
        return block;
    }

    private static Border Divider()
    {
        var divider = new Border { Margin = new Thickness(0, 14, 0, 14) };
        divider.SetResourceReference(StyleProperty, "Suite.Divider");
        return divider;
    }

    private void Refresh()
    {
        tools.Children.Clear();
        toggles.Clear();
        retries.Clear();
        var first = true;
        foreach (var tool in controller.Tools)
        {
            if (!first) tools.Children.Add(Divider());
            first = false;
            tools.Children.Add(BuildToolGroup(tool));
        }
        themes.SelectedIndex = (int)controller.Settings.Theme;
        startWithWindows.IsChecked = controller.Settings.StartWithWindows;
        autoStartStatus.Text = controller.AutoStartError is { } error
            ? "开机启动未生效：" + error
            : controller.Settings.StartWithWindows
                ? "登录后只启动已启用的工具，窗口按套件保存的状态恢复。"
                : "登录后不会自动运行工具套件。";
        autoStartStatus.SetResourceReference(TextBlock.ForegroundProperty,
            controller.AutoStartError is null ? "SuiteInkTertiary" : "SuiteDanger");
    }

    private UIElement BuildToolGroup(ToolStatus tool)
    {
        var enabled = new CheckBox { IsChecked = tool.Enabled, IsEnabled = tool.State != ToolState.Unavailable };
        enabled.SetResourceReference(StyleProperty, "Suite.SwitchRow");
        var captions = new StackPanel();
        var name = new TextBlock { Text = tool.DisplayName, FontSize = 13 };
        name.SetResourceReference(TextBlock.ForegroundProperty, "SuiteInk");
        var state = new TextBlock { Text = StateLabel(tool), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        state.SetResourceReference(TextBlock.ForegroundProperty,
            tool.State switch
            {
                ToolState.Failed => "SuiteDanger",
                ToolState.Unavailable => "SuiteInkDisabled",
                _ => "SuiteInkSecondary"
            });
        captions.Children.Add(name);
        captions.Children.Add(state);
        enabled.Content = captions;
        enabled.Click += async (_, _) => await controller.SetEnabledAsync(tool.Id, enabled.IsChecked == true);
        toggles[tool.Id] = enabled;

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (tool.State == ToolState.Failed)
        {
            // 失败不伪装运行：给出可操作提示，并可原地重试启用（重试前由控制器清理残留）。
            var retry = Pill("重试启动");
            retry.Click += async (_, _) => await controller.SetEnabledAsync(tool.Id, true);
            retries[tool.Id] = retry;
            actions.Children.Add(retry);
        }
        foreach (var command in tool.Commands.Where(c => !c.RequiresRunning))
        {
            var button = Pill(command.Label);
            button.Click += async (_, _) => await controller.ExecuteCommandAsync(tool.Id, command.Id);
            actions.Children.Add(button);
        }

        var group = new StackPanel();
        group.Children.Add(enabled);
        if (actions.Children.Count > 0) group.Children.Add(actions);
        return group;
    }

    private static Button Pill(string text)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
        button.SetResourceReference(StyleProperty, "Suite.GhostButton");
        return button;
    }

    internal CheckBox? ToolToggleForVerification(string id) => toggles.GetValueOrDefault(id);
    internal Button? RetryButtonForVerification(string id) => retries.GetValueOrDefault(id);
    internal ComboBox ThemeSelectorForVerification => themes;
    internal CheckBox StartWithWindowsToggleForVerification => startWithWindows;
    internal TextBlock AutoStartStatusForVerification => autoStartStatus;
    internal static string StateLabel(ToolStatus tool) => ToolStateText.Detail(tool);
}
