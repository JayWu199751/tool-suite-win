using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToolSuite.Core;
using Forms = System.Windows.Forms;

namespace ToolSuite.Reminders;

public sealed class ReminderWindow : Window
{
    private readonly ReminderSchedule schedule;
    private readonly StackPanel rows = new();
    private readonly TextBlock subtitle = ReminderUi.Text("", 12, true);
    private readonly Grid layout = new() { Margin = new Thickness(20) };
    private readonly Forms.Screen screen;
    private Reminder[] displayed = [];
    private bool silent;
    public event Action<bool>? Dismissed;

    public ReminderWindow(ReminderSchedule schedule, ThemePreference theme)
    {
        this.schedule = schedule;
        screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        Title = "循环提醒";
        Width = 380; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = true;
        UseLayoutRounding = true;
        AutomationProperties.SetName(this, "循环提醒：等待确认");
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel();
        var title = ReminderUi.Text("提醒时间到了", 21); title.FontWeight = FontWeights.SemiBold;
        labels.Children.Add(title);
        subtitle.Margin = new Thickness(0, 8, 0, 0); labels.Children.Add(subtitle);
        heading.Children.Add(labels);
        var close = ReminderUi.Button("×", Close);
        close.MinWidth = 32; close.Padding = new Thickness(8, 4, 8, 4); close.Margin = new Thickness(8, 0, 0, 0);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.ToolTip = "全部确认并关闭，重新开始计时";
        AutomationProperties.SetName(close, "全部确认并关闭");
        Grid.SetColumn(close, 1); heading.Children.Add(close);
        layout.Children.Add(heading);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var footer = ReminderUi.Text("点击“知道了”后，开始下一轮计时。", 12, true);
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        var outer = new Border { Child = layout, CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(1) };
        outer.SetResourceReference(Border.BackgroundProperty, "Reminder.Canvas");
        outer.SetResourceReference(Border.BorderBrushProperty, "Reminder.Line");
        Content = outer;
        ApplyTheme(theme);
        UpdatePending();
        SourceInitialized += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
        Closed += (_, _) => Dismissed?.Invoke(silent);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    public void ApplyTheme(ThemePreference theme)
    {
        ReminderUi.Apply(this, theme);
        layout.LayoutTransform = new ScaleTransform(ReminderUi.TextScale, ReminderUi.TextScale);
        if (IsVisible) Reposition();
    }

    public void UpdatePending()
    {
        var pending = schedule.Pending.ToArray();
        if (pending.SequenceEqual(displayed)) return;
        var restoreFocus = IsKeyboardFocusWithin;
        var focusedId = Keyboard.FocusedElement is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";
        var confirmations = new List<System.Windows.Controls.Button>();
        displayed = pending;
        rows.Children.Clear();
        subtitle.Text = $"{pending.Length} 项提醒等待确认";
        foreach (var item in pending)
        {
            var panel = new StackPanel();
            var name = ReminderUi.Text(item.Name, 17); name.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(name);
            var detail = ReminderUi.Text("每 " + ReminderUi.Interval(item.IntervalSeconds), 12, true);
            detail.Margin = new Thickness(0, 4, 0, 12); panel.Children.Add(detail);
            var confirm = ReminderUi.Button("知道了", () => schedule.Acknowledge([item.Id]));
            AutomationProperties.SetName(confirm, "知道了：" + item.Name);
            AutomationProperties.SetAutomationId(confirm, "confirm-" + item.Id);
            confirmations.Add(confirm);
            confirm.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            panel.Children.Add(confirm);
            rows.Children.Add(ReminderUi.Card(panel));
        }
        if (restoreFocus && confirmations.Count > 0)
        {
            var next = confirmations.FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == focusedId) ?? confirmations[0];
            Dispatcher.BeginInvoke(() => { if (IsVisible && next.IsLoaded) next.Focus(); });
        }
    }

    public void Reposition() => ReminderDesktop.Position(this, Forms.Screen.FromRectangle(screen.Bounds));
    public void DismissSilently() { silent = true; Close(); }
}
