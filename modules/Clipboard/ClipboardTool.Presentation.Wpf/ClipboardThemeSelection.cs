using ToolSuite.Core;

namespace ClipboardTool.Presentation.Wpf;

/// <summary>剪贴板模块主题字典的唯一选择入口：系统高对比度优先于明暗偏好。
/// 纯函数，便于无头验证；字典本身只替换模块局部资源，不触碰宿主 Application 字典。</summary>
public static class ClipboardThemeSelection
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string HighContrast = "HighContrast";

    /// <summary>主题字典名：高对比度 > 深色 > 浅色（跟随系统无高对比度时按宿主下发的有效浅色）。</summary>
    public static string NameFor(ThemePreference theme, bool highContrast) =>
        highContrast ? HighContrast : theme == ThemePreference.Dark ? Dark : Light;

    public static Uri SourceFor(ThemePreference theme, bool highContrast) => new(
        $"/ClipboardTool;component/Themes/{NameFor(theme, highContrast)}.xaml", UriKind.Relative);
}
