using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToolSuite.Core;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace ToolSuite.Reminders;

/// <summary>新增与编辑提醒的独立窗口，不创建或依附统一设置窗口。</summary>
public sealed class ReminderEditorWindow : Window
{
    private readonly ReminderModule module;
    private readonly Guid? reminderId;
    private readonly StackPanel body = new() { Margin = new Thickness(24) };
    private readonly TextBox name = new() { MaxLength = 120 };
    private readonly TextBox amount = new();
    private readonly List<RadioButton> units = [];
    private readonly TextBlock feedback = ReminderUi.Text("", 12);
    private readonly TextBlock state = ReminderUi.Text("", 12, true);
    private readonly Button save;
    private bool initializing = true;
    private bool closed;

    public ReminderEditorWindow(ReminderModule module, Reminder? reminder)
    {
        this.module = module;
        reminderId = reminder?.Id;
        Title = reminder is null ? "新增提醒" : "编辑提醒";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.Height;
        MinWidth = 320; ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true; UseLayoutRounding = true;
        AutomationProperties.SetName(this, Title);
        ApplyTheme(module.Theme);
        SetResourceReference(BackgroundProperty, "Reminder.Canvas");
        var title = ReminderUi.Text(Title, 21);
        title.FontWeight = FontWeights.SemiBold;
        body.Children.Add(title);
        var description = ReminderUi.Text("设置一个间隔，到期提醒；确认后重新计时。", 14, true);
        description.Margin = new Thickness(0, 8, 0, 20); body.Children.Add(description);
        var fields = new StackPanel();
        fields.Children.Add(ReminderUi.Text("提醒名称"));
        name.Text = reminder?.Name ?? "";
        name.SetResourceReference(StyleProperty, "Reminder.Input");
        name.Margin = new Thickness(0, 8, 0, 20);
        name.ToolTip = "例如：喝水、站起来活动或休息";
        AutomationProperties.SetName(name, "提醒名称");
        fields.Children.Add(name);
        fields.Children.Add(ReminderUi.Text("每隔多久提醒"));
        var seconds = reminder?.IntervalSeconds ?? 1800;
        var selectedUnit = seconds % 3600 == 0 ? 2 : seconds % 60 == 0 ? 1 : 0;
        amount.Text = (seconds / (selectedUnit == 2 ? 3600 : selectedUnit == 1 ? 60 : 1)).ToString(CultureInfo.CurrentCulture);
        amount.SetResourceReference(StyleProperty, "Reminder.Input");
        amount.Margin = new Thickness(0, 8, 0, 8);
        AutomationProperties.SetName(amount, "提醒间隔数值");
        fields.Children.Add(amount);
        var unitRow = new WrapPanel();
        foreach (var label in new[] { "秒", "分钟", "小时" })
        {
            var unit = new RadioButton { Content = label, GroupName = "interval-unit", MinHeight = 32, MinWidth = 64,
                Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
            unit.SetResourceReference(ForegroundProperty, "Reminder.Ink");
            unit.SetResourceReference(FocusVisualStyleProperty, "Reminder.Focus");
            AutomationProperties.SetName(unit, "间隔单位：" + label);
            units.Add(unit); unitRow.Children.Add(unit);
            unit.Checked += (_, _) => ValidateForm();
        }
        units[selectedUnit].IsChecked = true;
        fields.Children.Add(unitRow);
        body.Children.Add(ReminderUi.Card(fields));
        feedback.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        body.Children.Add(feedback);
        state.Margin = new Thickness(0, 0, 0, 16); body.Children.Add(state);
        var actions = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        actions.Children.Add(ReminderUi.Button("取消", Close));
        save = ReminderUi.Button(reminder is null ? "创建提醒" : "保存修改", Save);
        save.IsDefault = true;
        actions.Children.Add(save); body.Children.Add(actions);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(BackgroundProperty, "Reminder.Canvas");
        Content = scroll;
        name.TextChanged += (_, _) => ValidateForm();
        amount.TextChanged += (_, _) => ValidateForm();
        module.Schedule.Changed += UpdateState;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) =>
        {
            closed = true;
            module.Schedule.Changed -= UpdateState;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        };
        Loaded += (_, _) => { name.Focus(); name.CaretIndex = name.Text.Length; };
        SourceInitialized += (_, _) => ApplyTheme(module.Theme);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        initializing = false;
        UpdateState(); ValidateForm(showEmptyName: false);
    }

    private int ReadIntervalSeconds()
    {
        if (!decimal.TryParse(amount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
            || value <= 0 || value > int.MaxValue)
            throw new ArgumentException("请输入大于零的提醒间隔，例如 30 分钟。");
        var seconds = value * (units[2].IsChecked == true ? 3600 : units[1].IsChecked == true ? 60 : 1);
        if (seconds < 1 || seconds > int.MaxValue || seconds != decimal.Truncate(seconds))
            throw new ArgumentException("间隔需为整秒，范围为 1～2147483647 秒；请调整数值或单位。");
        return (int)seconds;
    }

    private void ValidateForm(bool showEmptyName = true)
    {
        if (initializing) return;
        try
        {
            EnsureReminderExists();
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                save.IsEnabled = false;
                feedback.Text = showEmptyName ? "请填写提醒名称。" : "";
                return;
            }
            ReadIntervalSeconds(); feedback.Text = ""; save.IsEnabled = true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { feedback.Text = error.Message; save.IsEnabled = false; }
    }

    private void EnsureReminderExists()
    {
        if (reminderId is { } id && !module.Schedule.Reminders.Any(r => r.Id == id))
            throw new InvalidOperationException("这项提醒已被删除，请关闭此窗口后重新选择。");
    }

    private void UpdateState()
    {
        state.Text = module.Schedule.IsRunning ? "保存后生效，提醒会在后台运行。"
            : "循环提醒工具已停用。可以先保存，启用工具后开始计时。";
        if (!initializing) ValidateForm(showEmptyName: false);
    }

    private void Save()
    {
        try
        {
            EnsureReminderExists();
            var enabled = reminderId is { } id ? module.Schedule.Reminders.Single(r => r.Id == id).Enabled : true;
            module.Schedule.Save(reminderId, name.Text, ReadIntervalSeconds(), enabled);
            Close();
        }
        catch (Exception error) { feedback.Text = "未保存：" + error.Message; }
    }

    public void ApplyTheme(ThemePreference theme)
    {
        ReminderUi.Apply(this, theme);
        ReminderDesktop.SetDarkTitleBar(this, theme == ThemePreference.Dark);
        var scale = ReminderUi.TextScale;
        body.LayoutTransform = new ScaleTransform(scale, scale);
        MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height - 32);
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 32);
        Width = Math.Min(440 * scale, MaxWidth);
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
        => Dispatcher.BeginInvoke(() => { if (!closed) ApplyTheme(module.Theme); });
}
