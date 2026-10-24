using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using SystemColors = System.Windows.SystemColors;
using FontFamily = System.Windows.Media.FontFamily;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace ToolSuite.App;

/// <summary>托盘右键菜单：用 WPF ContextMenu，不再走 WinForms GDI 自绘。
/// 皮肤只在菜单自己的资源树里（Themes/TrayMenu.xaml），不会渗到模块窗口；
/// 画刷全部 DynamicResource，主题与高对比度变化即时跟随，无需重绘代码。
/// 打开时把前台转到弹出层 HWND，点击菜单外任意位置才能触发系统级收起。</summary>
internal sealed class TrayMenu : ContextMenu
{
    private const int MinWidthDip = 208;

    /// <summary>从未收起过时 <see cref="ClosedAtTick"/> 的初值。</summary>
    internal const long Never = long.MinValue;

    public TrayMenu()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ToolSuite;component/Themes/TrayMenu.xaml", UriKind.Absolute),
        });
        // 弹出层根元素（同 Window）不会被自身 Resources 里的隐式样式命中，必须显式套用，
        // 否则整块退回系统默认的浅色菜单模板：方角、灰描边、亮色分隔线与浅色滚动条。
        Style = Resources["Suite.TrayMenu"] as Style;
        // 从托盘图标弹出：光标位置即锚点，不用自己换算 DPI。
        Placement = PlacementMode.MousePoint;
        StaysOpen = false;
        MinWidth = MinWidthDip;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
    }

    /// <summary>最近一次收起的 TickCount64；从未收起为 <see cref="Never"/>。
    /// TrayHost 用它判断"这记右键刚把菜单顶下去"，防止收起后立刻重开。</summary>
    internal long ClosedAtTick { get; private set; } = Never;

    /// <summary>弹出层窗口句柄；菜单未打开（视觉树未接入窗口源）时为 <see cref="IntPtr.Zero"/>。</summary>
    internal IntPtr PopupHandle =>
        PresentationSource.FromVisual(this) is HwndSource source ? source.Handle : IntPtr.Zero;

    protected override void OnOpened(RoutedEventArgs e)
    {
        base.OnOpened(e);
        // 从托盘弹出的菜单进程没有前台，WPF 的失活收起机制不生效：点桌面、其他应用
        // 菜单都不收起。把前台转给弹出层自己的 HWND，点击外部才会触发收起
        // （Clipboard 模块 TrayIconHost 的同类先例）。
        var handle = PopupHandle;
        if (handle != IntPtr.Zero) _ = NativeMethods.SetForegroundWindow(handle);
    }

    protected override void OnClosed(RoutedEventArgs e)
    {
        ClosedAtTick = Environment.TickCount64;
        base.OnClosed(e);
    }

    /// <summary>外观变化时由 <see cref="TrayHost"/> 下发画刷。
    /// 行内样式与子项仍走 DynamicResource 跟随 Suite* 键；这里把浮层两色写成本地值，
    /// 因为菜单未打开时也要能读到当前主题色（验证入口依赖），且本地值优先于样式。
    /// 高对比度交还系统配色，不改写用户的对比度主题（docs/design-system.md §6）。</summary>
    public void ApplyAppearance(bool highContrast)
    {
        var resources = System.Windows.Application.Current?.Resources;
        Background = highContrast ? SystemColors.WindowBrush : BrushOf(resources, "SuiteCard", SystemColors.WindowBrush);
        Foreground = highContrast ? SystemColors.WindowTextBrush : BrushOf(resources, "SuiteInk", SystemColors.WindowTextBrush);
    }

    private static Brush BrushOf(ResourceDictionary? resources, string key, Brush fallback) =>
        resources is not null && resources[key] is Brush brush ? brush : fallback;

    /// <summary>组标题：工具名用正文色，状态用次要色，让"名称 · 状态"有层次而不是一整行同色。</summary>
    public static TextBlock GroupHeader(string name, string state)
    {
        var text = new TextBlock();
        text.Inlines.Add(new Run(name));
        if (state.Length > 0)
        {
            var meta = new Run(" · " + state);
            meta.SetResourceReference(TextElement.ForegroundProperty, "SuiteInkSecondary");
            text.Inlines.Add(meta);
        }
        return text;
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);
}
