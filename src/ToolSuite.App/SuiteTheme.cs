using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using ToolSuite.Core;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace ToolSuite.App;

/// <summary>只更新宿主自身资源；模块用自己的局部资源，通过 ApplyTheme 接收统一有效主题。</summary>
internal sealed class SuiteTheme : IDisposable
{
    private readonly SuiteController controller;
    private readonly System.Windows.Threading.Dispatcher dispatcher;
    public SuiteTheme(SuiteController controller, System.Windows.Threading.Dispatcher dispatcher)
    {
        this.controller = controller; this.dispatcher = dispatcher;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        controller.Changed += Refresh;
        Refresh();
    }
    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => dispatcher.BeginInvoke(Refresh);
    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => dispatcher.BeginInvoke(Refresh);
    private void Refresh()
    {
        var dark = controller.Settings.Theme == ThemePreference.Dark;
        if (controller.Settings.Theme == ThemePreference.System)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        var resources = System.Windows.Application.Current.Resources;
        resources["SuiteBackground"] = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(250, 250, 250));
        resources["SuiteForeground"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(dark ? Colors.White : Colors.Black);
        controller.ApplyEffectiveTheme(dark ? ThemePreference.Dark : ThemePreference.Light);
    }
    public void Dispose()
    {
        controller.Changed -= Refresh;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }
}
