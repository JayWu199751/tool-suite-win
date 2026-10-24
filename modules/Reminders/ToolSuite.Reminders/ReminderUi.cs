using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ToolSuite.Core;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;

namespace ToolSuite.Reminders;

internal static class ReminderUi
{
    public static void Apply(FrameworkElement target, ThemePreference theme)
    {
        if (target.Resources.MergedDictionaries.Count == 0)
            target.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/ToolSuite.Reminders;component/ReminderControls.xaml", UriKind.Relative) });
        var dark = theme == ThemePreference.Dark;
        var contrast = SystemParameters.HighContrast;
        Set("Canvas", dark ? "#0B0B0E" : "#F5F5F7", SystemColors.WindowBrush);
        Set("Paper", dark ? "#141419" : "#FFFFFF", SystemColors.WindowBrush);
        Set("Well", dark ? "#15151A" : "#F5F5F7", SystemColors.ControlBrush);
        Set("Ink", dark ? "#F2F2F5" : "#1D1D1F", SystemColors.WindowTextBrush);
        Set("Secondary", dark ? "#A0A0AC" : "#63636A", SystemColors.WindowTextBrush);
        Set("Link", dark ? "#A7A4F0" : "#0066CC", SystemColors.HotTrackBrush);
        Set("FocusBrush", dark ? "#7B77E0" : "#0071E3", SystemColors.HighlightBrush);
        Set("Line", dark ? "#757575" : "#B8B8BD", SystemColors.ControlTextBrush);
        Set("Hover", dark ? "#17FFFFFF" : "#E8E8ED", SystemColors.ControlBrush);
        Set("Thumb", dark ? "#F2F2F5" : "#FFFFFF", SystemColors.HighlightTextBrush);
        if (target is Control control)
        {
            control.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
            control.FontSize = 14;
            control.SetResourceReference(Control.ForegroundProperty, "Reminder.Ink");
        }
        void Set(string role, string hex, Brush system)
            => target.Resources["Reminder." + role] = contrast ? system : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public static double TextScale
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
                return key?.GetValue("TextScaleFactor") is int value ? Math.Clamp(value / 100d, 1, 2.25) : 1;
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return 1; }
        }
    }

    public static TextBlock Text(string value, double size = 14, bool secondary = false)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "Reminder.Secondary" : "Reminder.Ink");
        return text;
    }

    public static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Reminder.Button");
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    public static Border Card(UIElement body)
    {
        var border = new Border { Child = body, CornerRadius = new CornerRadius(20), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) };
        border.SetResourceReference(Border.BackgroundProperty, "Reminder.Paper");
        return border;
    }

    public static string Interval(int seconds) => seconds % 3600 == 0 ? $"{seconds / 3600} 小时"
        : seconds % 60 == 0 ? $"{seconds / 60} 分钟" : $"{seconds} 秒";

    public static string Status(ReminderSchedule schedule, Reminder item)
    {
        if (!item.Enabled) return "已停用";
        if (!schedule.IsRunning) return "工具停用 · 启用后开始计时";
        if (schedule.Remaining(item.Id) is not { } left) return "等待确认";
        var seconds = (long)Math.Ceiling(left.TotalSeconds);
        return seconds >= 3600 ? $"还剩 {seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"还剩 {seconds / 60:00}:{seconds % 60:00}";
    }
}
