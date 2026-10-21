using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using ToolSuite.Core;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using SystemColors = System.Windows.SystemColors;

namespace ToolSuite.App;

/// <summary>只更新宿主自身资源；模块用自己的局部资源，通过 ApplyTheme 接收统一有效主题。
/// 仅在有效外观（明暗/高对比度）变化时重建，普通命令与启停状态变化不触发。
/// 画刷取值见 docs/design-system.md，与剪贴板、性能、便签三处模块字典保持一致。</summary>
internal sealed class SuiteTheme : IDisposable
{
    private const string HostDictionaryUri = "/ToolSuite;component/Themes/HostControls.xaml";

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
        // 控件样式与画刷分两个字典：样式只写一次，画刷每次外观变化覆盖取值。
        var styles = HostStyles;
        if (!resources.MergedDictionaries.Contains(styles)) resources.MergedDictionaries.Add(styles);
        foreach (var (key, brush) in Tokens(dark, highContrast)) resources[key] = brush;
        controller.ApplyEffectiveTheme(dark ? ThemePreference.Dark : ThemePreference.Light);
        AppearanceApplied?.Invoke(dark, highContrast);
    }

    /// <summary>宿主控件模板；只依赖 Pack URI，不触碰模块资源。</summary>
    private static ResourceDictionary? hostStyles;
    private static ResourceDictionary HostStyles => hostStyles ??=
        new ResourceDictionary { Source = new Uri(HostDictionaryUri, UriKind.Relative) };

    /// <summary>高对比度沿用系统色，不改写用户的对比度主题。</summary>
    private static IEnumerable<(string Key, Brush Brush)> Tokens(bool dark, bool highContrast)
    {
        if (highContrast)
        {
            yield return ("SuiteBackground", SystemColors.WindowBrush);
            yield return ("SuiteCard", SystemColors.WindowBrush);
            yield return ("SuiteForeground", SystemColors.WindowTextBrush);
            yield return ("SuiteInk", SystemColors.WindowTextBrush);
            yield return ("SuiteInkSecondary", SystemColors.WindowTextBrush);
            yield return ("SuiteInkTertiary", SystemColors.GrayTextBrush);
            yield return ("SuiteInkDisabled", SystemColors.GrayTextBrush);
            yield return ("SuiteHairline", SystemColors.ControlDarkBrush);
            yield return ("SuiteStrong", SystemColors.ControlDarkBrush);
            yield return ("SuiteHover", SystemColors.ControlBrush);
            yield return ("SuiteWell", SystemColors.ControlBrush);
            yield return ("SuiteAccent", SystemColors.HighlightBrush);
            yield return ("SuiteAccentText", SystemColors.HighlightTextBrush);
            yield return ("SuiteLink", SystemColors.HotTrackBrush);
            yield return ("SuiteDanger", SystemColors.WindowTextBrush);
            yield return ("SuiteSuccess", SystemColors.WindowTextBrush);
            yield return ("SuiteSwitchTrack", SystemColors.ControlBrush);
            yield return ("SuiteSwitchThumb", SystemColors.WindowBrush);
            yield break;
        }
        yield return ("SuiteBackground", Rgb(dark ? 0x00 : 0xF5, dark ? 0x00 : 0xF5, dark ? 0x00 : 0xF7));
        yield return ("SuiteCard", Rgb(dark ? 0x1D : 0xFF, dark ? 0x1D : 0xFF, dark ? 0x1F : 0xFF));
        yield return ("SuiteForeground", Rgb(dark ? 0xF5 : 0x1D, dark ? 0xF5 : 0x1D, dark ? 0xF7 : 0x1F));
        yield return ("SuiteInk", Rgb(dark ? 0xF5 : 0x1D, dark ? 0xF5 : 0x1D, dark ? 0xF7 : 0x1F));
        yield return ("SuiteInkSecondary", Rgb(dark ? 0x86 : 0x63, dark ? 0x86 : 0x63, dark ? 0x8B : 0x6A));
        yield return ("SuiteInkTertiary", Rgb(dark ? 0xA1 : 0x75, dark ? 0xA1 : 0x75, dark ? 0xA6 : 0x7A));
        yield return ("SuiteInkDisabled", Rgb(dark ? 0x55 : 0xA1, dark ? 0x55 : 0xA1, dark ? 0x5C : 0xA6));
        yield return ("SuiteHairline", Rgb(dark ? 0x33 : 0xD6, dark ? 0x33 : 0xD6, dark ? 0x36 : 0xD6));
        yield return ("SuiteStrong", Rgb(dark ? 0x4A : 0xB8, dark ? 0x4A : 0xB8, dark ? 0x4E : 0xBD));
        yield return ("SuiteHover", Rgb(dark ? 0x2A : 0xE8, dark ? 0x2A : 0xE8, dark ? 0x2C : 0xED));
        yield return ("SuiteWell", Rgb(dark ? 0x00 : 0xF5, dark ? 0x00 : 0xF5, dark ? 0x00 : 0xF7));
        yield return ("SuiteAccent", Rgb(0x00, 0x71, 0xE3));
        yield return ("SuiteAccentText", Rgb(0xFF, 0xFF, 0xFF));
        yield return ("SuiteLink", Rgb(dark ? 0x29 : 0x00, dark ? 0x97 : 0x66, dark ? 0xFF : 0xCC));
        yield return ("SuiteDanger", Rgb(dark ? 0xFF : 0xC7, dark ? 0x45 : 0x26, dark ? 0x3A : 0x1F));
        yield return ("SuiteSuccess", Rgb(dark ? 0x30 : 0x1D, dark ? 0xD1 : 0x83, dark ? 0x58 : 0x48));
        yield return ("SuiteSwitchTrack", Rgb(dark ? 0x3A : 0xE8, dark ? 0x3A : 0xE8, dark ? 0x3D : 0xED));
        yield return ("SuiteSwitchThumb", Rgb(0xFF, 0xFF, 0xFF));
    }

    private static SolidColorBrush Rgb(int red, int green, int blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)red, (byte)green, (byte)blue));
        brush.Freeze();
        return brush;
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
