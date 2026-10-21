using System.Windows;
using System.Windows.Media;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using Color = System.Windows.Media.Color;
namespace PerfMonitor.App.Shell;

/// <summary>
/// 托盘/性能条共享菜单的亮暗主题皮肤：XAML 侧定义模板与默认（暗色）画刷，
/// Apply(dark) 按有效主题逐键覆盖画刷（同设置窗 ApplyTheme 的模式），
/// 模板里的 DynamicResource 引用让开着的菜单也即时跟随切换。
/// 视觉规范要求零投影，菜单不再保留投影留白，定位偏移归零。
/// </summary>
public partial class ShellContextMenuTheme : ResourceDictionary
{
    public const string SeparatorStyleKey = "ShellMenuSeparatorStyle";

    private const string BackgroundKey = "ShellMenuBackgroundBrush";
    private const string ForegroundKey = "ShellMenuForegroundBrush";
    private const string SecondaryKey = "ShellMenuSecondaryBrush";
    private const string CheckKey = "ShellMenuCheckBrush";
    private const string HoverKey = "ShellMenuHoverBrush";
    private const string BorderKey = "ShellMenuBorderBrush";
    private const string DividerKey = "ShellMenuDividerBrush";

    private readonly WpfContextMenu _menu;
    private bool _dark;
    private bool _applied;

    public ShellContextMenuTheme(WpfContextMenu menu, bool dark)
    {
        InitializeComponent();
        _menu = menu;
        menu.Resources.MergedDictionaries.Add(this);
        menu.Style = (Style)this["ShellMenuRootStyle"];
        menu.ItemContainerStyle = (Style)this["ShellMenuItemStyle"];
        // 无投影留白：popup 左上角即鼠标点，两个方向的偏移都归零。
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
        Apply(dark);
    }

    /// <summary>按暗色与否覆盖画刷；同一主题重复调用是空操作。</summary>
    public void Apply(bool dark)
    {
        if (_applied && _dark == dark)
        {
            return;
        }

        _dark = dark;
        _applied = true;
        // 暗色取 docs/UI Design/Apple Dark.md：炭黑浮层 + 12% 白描边 + 9% 白悬停 + 6% 白发丝分隔；
        // 勾选与强调只用靛蓝。亮色保持原取值。
        _menu.Resources[BackgroundKey] = Brush(dark ? 0xFF141419L : 0xFFFFFFFFL);
        _menu.Resources[ForegroundKey] = Brush(dark ? 0xFFF2F2F5L : 0xFF1D1D1FL);
        _menu.Resources[SecondaryKey] = Brush(dark ? 0xFFA0A0ACL : 0xFF63636AL);
        _menu.Resources[CheckKey] = Brush(dark ? 0xFF7B77E0L : 0xFF0071E3L);
        _menu.Resources[HoverKey] = Brush(dark ? 0x17FFFFFFL : 0x0A000000L);
        _menu.Resources[BorderKey] = Brush(dark ? 0x1AFFFFFFL : 0xFFD6D6D6L);
        _menu.Resources[DividerKey] = Brush(dark ? 0x0FFFFFFFL : 0xFFE8E8EDL);
    }

    /// <summary>勾选行与动作项用正文色，未勾的 checkable 行用标签色。</summary>
    public static void ApplyForeground(WpfMenuItem item, bool isCheckable, bool isChecked)
    {
        item.SetResourceReference(
            WpfMenuItem.ForegroundProperty,
            isCheckable && !isChecked ? SecondaryKey : ForegroundKey);
    }

    private static SolidColorBrush Brush(long argb) =>
        Brush((byte)(argb >> 24 & 0xFF), (byte)(argb >> 16 & 0xFF), (byte)(argb >> 8 & 0xFF), (byte)(argb & 0xFF));

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
