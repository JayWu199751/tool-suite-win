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
            yield return ("SuiteMenuBorder", SystemColors.ControlDarkBrush);
            yield return ("SuiteMenuDivider", SystemColors.ControlDarkBrush);
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
        // 暗色取 docs/UI Design/Apple Dark.md：近黑画布 + 炭黑卡片 + 中性阶梯 + 单一家族靛蓝；
        // 描边/悬停/选中沿用参考稿的 alpha 口径（6% / 9% / 12% 白），亮色保持原取值。
        yield return ("SuiteBackground", dark ? Rgb(0x0B, 0x0B, 0x0E) : Rgb(0xF5, 0xF5, 0xF7));
        yield return ("SuiteCard", dark ? Rgb(0x14, 0x14, 0x19) : Rgb(0xFF, 0xFF, 0xFF));
        yield return ("SuiteForeground", dark ? Rgb(0xF2, 0xF2, 0xF5) : Rgb(0x1D, 0x1D, 0x1F));
        yield return ("SuiteInk", dark ? Rgb(0xF2, 0xF2, 0xF5) : Rgb(0x1D, 0x1D, 0x1F));
        yield return ("SuiteInkSecondary", dark ? Rgb(0xA0, 0xA0, 0xAC) : Rgb(0x63, 0x63, 0x6A));
        yield return ("SuiteInkTertiary", dark ? Rgb(0x7E, 0x7E, 0x8A) : Rgb(0x75, 0x75, 0x7A));
        yield return ("SuiteInkDisabled", dark ? Rgb(0x55, 0x55, 0x5F) : Rgb(0xA1, 0xA1, 0xA6));
        yield return ("SuiteHairline", dark ? Argb(0x0F, 0xFF, 0xFF, 0xFF) : Rgb(0xD6, 0xD6, 0xD6));
        // 菜单浮层：暗色描边取 Apple Dark 的 12% 白（6% 在壁纸上界定不住边界），分隔线仍用 6%。
        yield return ("SuiteMenuBorder", dark ? Argb(0x1F, 0xFF, 0xFF, 0xFF) : Rgb(0xD6, 0xD6, 0xD6));
        yield return ("SuiteMenuDivider", dark ? Argb(0x0F, 0xFF, 0xFF, 0xFF) : Rgb(0xE8, 0xE8, 0xED));
        yield return ("SuiteStrong", dark ? Argb(0x1F, 0xFF, 0xFF, 0xFF) : Rgb(0xB8, 0xB8, 0xBD));
        yield return ("SuiteHover", dark ? Argb(0x17, 0xFF, 0xFF, 0xFF) : Rgb(0xE8, 0xE8, 0xED));
        yield return ("SuiteWell", dark ? Rgb(0x15, 0x15, 0x1A) : Rgb(0xF5, 0xF5, 0xF7));
        yield return ("SuiteAccent", dark ? Rgb(0x7B, 0x77, 0xE0) : Rgb(0x00, 0x71, 0xE3));
        yield return ("SuiteAccentText", dark ? Rgb(0xA7, 0xA4, 0xF0) : Rgb(0xFF, 0xFF, 0xFF));
        yield return ("SuiteLink", dark ? Rgb(0xA7, 0xA4, 0xF0) : Rgb(0x00, 0x66, 0xCC));
        yield return ("SuiteDanger", dark ? Rgb(0xE0, 0x5A, 0x52) : Rgb(0xC7, 0x26, 0x1F));
        yield return ("SuiteSuccess", dark ? Rgb(0x57, 0xC0, 0x8A) : Rgb(0x1D, 0x83, 0x48));
        yield return ("SuiteSwitchTrack", dark ? Argb(0x17, 0xFF, 0xFF, 0xFF) : Rgb(0xE8, 0xE8, 0xED));
        yield return ("SuiteSwitchThumb", dark ? Rgb(0xF2, 0xF2, 0xF5) : Rgb(0xFF, 0xFF, 0xFF));
    }

    private static SolidColorBrush Rgb(int red, int green, int blue) => Argb(0xFF, red, green, blue);

    private static SolidColorBrush Argb(int alpha, int red, int green, int blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)alpha, (byte)red, (byte)green, (byte)blue));
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
