using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

namespace ToolSuite.Reminders;

/// <summary>统一设置中的提醒列表；新增与编辑由独立窗口完成。卸载时释放界面刷新与事件。</summary>
public sealed class ReminderSettingsControl : UserControl
{
    private readonly ReminderModule module;
    private readonly StackPanel body = new();
    private readonly StackPanel rows = new();
    private readonly TextBlock feedback = ReminderUi.Text("", 12);
    private readonly Dictionary<Guid, TextBlock> statuses = [];
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool attached;

    public ReminderSettingsControl(ReminderModule module)
    {
        this.module = module;
        ReminderUi.Apply(this, module.Theme);
        SetResourceReference(BackgroundProperty, "Reminder.Paper");
        Content = body;
        body.Children.Add(ReminderUi.Text("启用期间，确认后重新计时；关闭设置不影响提醒。", 12, true));
        var create = ReminderUi.Button("新增提醒…", () => Try(() => module.OpenEditor()));
        create.Margin = new Thickness(0, 12, 0, 12);
        create.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        body.Children.Add(create);
        feedback.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        body.Children.Add(feedback);
        body.Children.Add(rows);
        refreshTimer.Tick += (_, _) => Try(RefreshStatuses);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        if (attached) return;
        attached = true;
        module.Schedule.Changed += RefreshRows;
        module.AppearanceChanged += ApplyAppearance;
        ApplyAppearance(); RefreshRows(); refreshTimer.Start();
    }

    private void Detach()
    {
        if (!attached) return;
        attached = false;
        refreshTimer.Stop();
        module.Schedule.Changed -= RefreshRows;
        module.AppearanceChanged -= ApplyAppearance;
    }

    private void ApplyAppearance()
    {
        ReminderUi.Apply(this, module.Theme);
        body.LayoutTransform = new ScaleTransform(ReminderUi.TextScale, ReminderUi.TextScale);
    }

    private void RefreshRows()
    {
        Try(() =>
        {
            var focusedId = Keyboard.FocusedElement is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";
            rows.Children.Clear(); statuses.Clear();
            var items = module.Schedule.Reminders;
            if (items.Count == 0) rows.Children.Add(ReminderUi.Text("还没有提醒。添加喝水、活动或休息提醒，按自己的节奏安排。", 14, true));
            foreach (var item in items)
            {
                var panel = new StackPanel();
                var title = ReminderUi.Text(item.Name, 17); title.FontWeight = FontWeights.SemiBold;
                panel.Children.Add(title);
                panel.Children.Add(ReminderUi.Text("每 " + ReminderUi.Interval(item.IntervalSeconds), 12, true));
                var status = ReminderUi.Text("", 17); status.Margin = new Thickness(0, 8, 0, 8);
                status.FontWeight = FontWeights.SemiBold;
                Typography.SetNumeralAlignment(status, FontNumeralAlignment.Tabular);
                panel.Children.Add(status); statuses[item.Id] = status;
                var enabled = new CheckBox { Content = "启用", IsChecked = item.Enabled };
                enabled.SetResourceReference(StyleProperty, "Reminder.Toggle");
                AutomationProperties.SetName(enabled, "启用提醒：" + item.Name);
                AutomationProperties.SetAutomationId(enabled, "enable-" + item.Id);
                enabled.Click += (_, _) =>
                {
                    Try(() => module.Schedule.Save(item.Id, item.Name, item.IntervalSeconds, enabled.IsChecked == true));
                    enabled.IsChecked = module.Schedule.Reminders.Single(r => r.Id == item.Id).Enabled;
                };
                panel.Children.Add(enabled);
                var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                var edit = ReminderUi.Button("编辑", () => Try(() => module.OpenEditor(item.Id)));
                AutomationProperties.SetName(edit, "编辑提醒：" + item.Name);
                AutomationProperties.SetAutomationId(edit, "edit-" + item.Id);
                actions.Children.Add(edit);
                var delete = ReminderUi.Button("删除", () =>
                {
                    if (MessageBox.Show(Window.GetWindow(this), $"删除“{item.Name}”？删除后将停止此项提醒。", "删除提醒",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
                    Try(() => module.Schedule.Delete(item.Id));
                });
                AutomationProperties.SetName(delete, "删除提醒：" + item.Name);
                actions.Children.Add(delete);
                panel.Children.Add(actions);
                // 设置分类已是 Paper，用 Canvas 色带区分行，避免卡片套卡片。
                var card = ReminderUi.Card(panel);
                card.SetResourceReference(Border.BackgroundProperty, "Reminder.Canvas");
                rows.Children.Add(card);
                foreach (var control in new System.Windows.Controls.Control[] { enabled, edit })
                    if (AutomationProperties.GetAutomationId(control) == focusedId) control.Focus();
            }
            RefreshStatuses();
        });
    }

    private void RefreshStatuses()
    {
        foreach (var item in module.Schedule.Reminders)
            if (statuses.TryGetValue(item.Id, out var text)) text.Text = ReminderUi.Status(module.Schedule, item);
    }

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception error) { feedback.Text = "未完成：" + error.Message; }
    }
}
