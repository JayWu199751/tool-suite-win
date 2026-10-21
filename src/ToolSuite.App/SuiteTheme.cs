using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using ToolSuite.Core;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace ToolSuite.App;

/// <summary>只更新宿主自身资源；模块用自己的局部资源，通过 ApplyTheme 接收统一有效主题。
/// 仅在有效外观（明暗/高对比度）变化时重建，普通命令与启停状态变化不触发。</summary>
internal sealed class SuiteTheme : IDisposable
{
    private readonly SuiteController controller;
    private readonly System.Windows.Threading.Dispatcher dispatcher;
    private (bool Dark, bool HighContrast)? applied;
    public event Action<bool, bool>? AppearanceApplied;
    public bool IsDark { get; private set; }
    public bool IsHighContrast { get; private set; }
    public SuiteTheme(SuiteController controller, System.Windows.Threading.Dispatcher dispatcher)
    {
        this.controller = controller; this.dispatcher = dispatcher;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        controller.AppearanceChanged += OnAppearanceChanged;
        Refresh();
    }
    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => dispatcher.BeginInvoke(new Action(Refresh));
    private void OnAppearanceChanged() => Refresh();
    // 系统文字大小/高对比度变化强制重算（模块主题由控制器去重，不会重复分配）。
    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => dispatcher.BeginInvoke(() => Refresh(force: true));
    private void Refresh() => Refresh(force: false);
    private void Refresh(bool force)
    {
        var (dark, highContrast) = Resolve();
        if (!force && applied == (dark, highContrast)) return;
        applied = (dark, highContrast);
        IsDark = dark;
        IsHighContrast = highContrast;
        var resources = System.Windows.Application.Current.Resources;
        resources["SuiteBackground"] = highContrast ? SystemColors.WindowBrush : new SolidColorBrush(dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(250, 250, 250));
        resources["SuiteForeground"] = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(dark ? Colors.White : Colors.Black);
        controller.ApplyEffectiveTheme(dark ? ThemePreference.Dark : ThemePreference.Light);
        AppearanceApplied?.Invoke(dark, highContrast);
    }
    private (bool Dark, bool HighContrast) Resolve()
    {
        var dark = controller.Settings.Theme == ThemePreference.Dark;
        if (controller.Settings.Theme == ThemePreference.System)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        return (dark, SystemParameters.HighContrast);
    }
    public void Dispose()
    {
        controller.AppearanceChanged -= OnAppearanceChanged;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }
}
