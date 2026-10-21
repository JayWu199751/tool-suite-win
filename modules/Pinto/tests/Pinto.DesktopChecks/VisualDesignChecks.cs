using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pinto.Core;
using Pinto.Services;
using Pinto.Views;

namespace Pinto.DesktopChecks;

internal static class VisualDesignChecks
{
    private static FrameworkElement _resourceScope = null!;
    public static async Task RunAsync(string root, Action<string, bool> check)
    {
        var state = new AppData { Notes = [new NoteData
        {
            Title = "本周计划 · 一个用于验证窄窗口的较长标题",
            Items = [
                new() { Text = "整理下一轮迭代的设计反馈，长内容应在窄窗口里完整换行，不被输入区域覆盖。" },
                new() { Text = "已完成的事项仍然易读", IsCompleted = true },
                new() { Text = new string('W', 90) },
                new() { Text = "最后一项可通过滚动访问" }
            ]
        }] };
        using var coordinator = new AppCoordinator(new DataStore(Path.Combine(root, "visual-data")), new(state, false));
        var window = coordinator.Windows.Values.Single();
        _resourceScope = window;
        try
        {
            foreach (var preference in new[] { ThemePreference.Light, ThemePreference.Dark })
            {
                coordinator.SetTheme(preference);
                var label = preference.ToString();
                // 暗色的悬停与描边是 alpha 淡染（9% / 6% 白），先按表面底色合成再算对比度。
                var canvas = Brush("SurfaceBrush").Color;
                check($"{label}：正文、辅助及完成文字在表面、输入与悬停色上均达到 4.5:1",
                    new[] { "TextBrush", "SecondaryBrush", "CompletedBrush" }.All(foreground =>
                        new[] { "SurfaceBrush", "InputBrush", "HoverBrush" }.All(background =>
                            Contrast(Over(Brush(foreground).Color, canvas), Over(Brush(background).Color, canvas)) >= 4.5)));
                check($"{label}：未完成圆圈及焦点环达到 3:1",
                    Contrast(Over(Brush("CircleBrush").Color, canvas), canvas) >= 3 &&
                    Contrast(Over(Brush("FocusBrush").Color, Brush("InputBrush").Color), Brush("InputBrush").Color) >= 3);
                check($"{label}：五种强调色与勾形均达到 3:1", Enum.GetValues<AccentColor>().All(accent =>
                {
                    var fill = ThemeService.GetAccentBrush(accent).Color;
                    return Contrast(fill, Brush("SurfaceBrush").Color) >= 3 && Contrast(fill, ((SolidColorBrush)window.Model.CheckmarkBrush).Color) >= 3;
                }));
                foreach (var scale in new[] { 1d, 2.25 })
                {
                    ThemeService.ApplyTextScale(window.Resources, scale);
                    foreach (var (width, height) in new[] { (252d, 198d), (324d, 273d), (720d, 480d) })
                    {
                        window.Width = width;
                        window.Height = height;
                        await Task.Delay(60);
                        window.UpdateLayout();
                        var title = (Grid)window.FindName("TitleBar");
                        var composer = (Border)window.FindName("AddItemBorder");
                        var scroller = (ScrollViewer)window.FindName("ItemsScroller");
                        var editor = (TextBox)window.FindName("NewItemEditor");
                        var headingBottom = title.TranslatePoint(new Point(0, title.ActualHeight), window).Y;
                        var listTop = scroller.TranslatePoint(new Point(), window).Y;
                        var listBottom = scroller.TranslatePoint(new Point(0, scroller.ActualHeight), window).Y;
                        var inputTop = composer.TranslatePoint(new Point(), window).Y;
                        check($"{label} {width}×{height} / {scale:P0}：标题、清单和输入不重叠，文字不横向溢出",
                            headingBottom <= listTop + 1 && listBottom <= inputTop + 1 && editor.ActualWidth > 40 &&
                            scroller.ViewportHeight > 20 && scroller.ExtentWidth <= scroller.ViewportWidth + 1 &&
                            inputTop + composer.ActualHeight <= window.ActualHeight - 8 &&
                            Descendants<TextBlock>((ItemsControl)window.FindName("ItemsList")).All(text => text.ActualHeight + 1 >= text.DesiredSize.Height));
                        if (width == 252 || width == 324)
                        {
                            scroller.ScrollToHome();
                            window.UpdateLayout();
                            Render(window, Path.Combine(root, $"layout-{label.ToLowerInvariant()}-{width}-{(int)(scale * 100)}.png"));
                        }
                    }
                }
                ThemeService.ApplyTextScale(window.Resources, 1);
                window.Width = 324; window.Height = 273;
                await Task.Delay(60);
                window.UpdateLayout();
                // 完成圆圈按「首行高度」居中，而不是钉在行顶：多行项与不同字号下都应与首行文字同心。
                var rows = Descendants<Border>((ItemsControl)window.FindName("ItemsList"))
                    .Where(element => element.Name == "ItemRow").ToArray();
                check($"{label}：每个清单项的完成圆圈与首行文字垂直居中",
                    rows.Length > 0 && rows.All(row =>
                    {
                        var circle = Descendants<CheckBox>(row).First();
                        var text = Descendants<TextBlock>(row).First(block => block.IsVisible && !string.IsNullOrEmpty(block.Text));
                        var circleCenter = circle.TranslatePoint(new Point(0, circle.ActualHeight / 2), window).Y;
                        var lineCenter = text.TranslatePoint(new Point(0, text.Padding.Top + FirstLineHeight(text) / 2), window).Y;
                        return Math.Abs(circleCenter - lineCenter) <= 1;
                    }));
                // 圆圈外缘（描边外沿）与标题文字左缘对齐：清单左栏不再比标题多缩进一截。
                var titleLeft = ((TextBlock)window.FindName("TitleLabel")).TranslatePoint(new Point(), window).X;
                check($"{label}：完成圆圈外缘与标题文字左缘对齐",
                    rows.Length > 0 && rows.All(row =>
                    {
                        var checkbox = Descendants<CheckBox>(row).First();
                        var circle = (System.Windows.Shapes.Ellipse)checkbox.Template.FindName("Circle", checkbox);
                        return Math.Abs(circle.TranslatePoint(new Point(), window).X - 0.625 - titleLeft) <= 1;
                    }));
                window.Activate();
                var newEditor = (TextBox)window.FindName("NewItemEditor");
                newEditor.Focus();
                window.UpdateLayout();
                var input = (Border)window.FindName("AddItemBorder");
                check($"{label}：输入框拥有键盘焦点与可见边框", newEditor.IsKeyboardFocused && Same(input.BorderBrush, Brush("FocusBrush")));
                var checkbox = Descendants<CheckBox>(window).First();
                checkbox.Focus();
                window.UpdateLayout();
                var focusRoot = (Border)checkbox.Template.FindName("CheckRoot", checkbox);
                check($"{label}：键盘到达完成控件，具有 28 DIP 命中区和焦点提示", checkbox.IsKeyboardFocused && checkbox.ActualWidth >= 28 && checkbox.ActualHeight >= 28 && Same(focusRoot.BorderBrush, Brush("FocusBrush")) && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(checkbox)));
                var next = checkbox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                check($"{label}：Tab 可在清单控件间继续导航", next && Keyboard.FocusedElement is CheckBox && !checkbox.IsKeyboardFocused);
                checkbox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(checkbox), 0, Key.Apps) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await Task.Delay(50);
                var row = Descendants<Border>((ItemsControl)window.FindName("ItemsList")).First(element => element.Name == "ItemRow");
                var menu = row.ContextMenu;
                check($"{label}：键盘项目菜单可访问编辑、排序与删除", menu?.IsOpen == true && menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "编辑")) && menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "删除清单项")));
                if (menu is not null) menu.IsOpen = false;
                var scrollerAtEnd = (ScrollViewer)window.FindName("ItemsScroller");
                scrollerAtEnd.ScrollToEnd();
                await Task.Delay(50);
                check($"{label}：最后一项可滚动访问", scrollerAtEnd.VerticalOffset > 0 && scrollerAtEnd.VerticalOffset + scrollerAtEnd.ViewportHeight >= scrollerAtEnd.ExtentHeight - 1);

                ThemeService.ApplyTextScale(window.Resources, 2.25);
                var dialog = new ImportDialog(3, 3);
                ThemeService.ApplyTextScale(dialog.Resources, 2.25);
                dialog.Show();
                await Task.Delay(60);
                dialog.UpdateLayout();
                check($"{label}：导入保留 Windows 标准窗口框架且标题栏同步主题", CornerChecks.HasNativeDialogAppearance(dialog));
                var append = (Button)dialog.FindName("AppendButton");
                var replace = (Button)dialog.FindName("ReplaceButton");
                var optionText = Descendants<TextBlock>(append).Concat(Descendants<TextBlock>(replace)).ToArray();
                check($"{label}：225% 文字下导入选项完整换行并保留取消入口", optionText.All(text => text.ActualHeight + text.Margin.Top + text.Margin.Bottom + 1 >= text.DesiredSize.Height) && Descendants<Button>(dialog).Any(button => button.IsCancel && button.IsVisible));
                Render(dialog, Path.Combine(root, $"import-{label.ToLowerInvariant()}-225.png"));
                ThemeService.ApplyTextScale(window.Resources, 1);
                ThemeService.ApplyTextScale(dialog.Resources, 1);
                await Task.Delay(60);
                Render(dialog, Path.Combine(root, $"import-{label.ToLowerInvariant()}-100.png"));
                dialog.Close();
            }

            // 单独加载系统颜色字典，不改动用户的 Windows 对比度主题设置。
            window.Resources.MergedDictionaries[0] = new() { Source = new Uri("pack://application:,,,/Pinto;component/Themes/HighContrast.xaml") };
            window.ApplyTheme();
            ThemeService.ApplyTextScale(window.Resources, 2.25);
            await Task.Delay(60);
            check("高对比度资源使用 Windows 表面、文字和选区颜色", Brush("SurfaceBrush").Color == SystemColors.WindowColor && Brush("TextBrush").Color == SystemColors.WindowTextColor && Brush("SelectionBrush").Color == SystemColors.HighlightColor);
            window.Model.Items.Clear();
            window.Width = 252; window.Height = 198;
            // IsEmpty 的变更由公开删除路径通知；使用最后一项触发空状态绑定。
            window.Model.NewItemText = "空状态检查"; window.Model.AddItem(); window.Model.RemoveItem(window.Model.Items.Single());
            await Task.Delay(60);
            var emptyScroller = (ScrollViewer)window.FindName("ItemsScroller");
            emptyScroller.ScrollToHome();
            check("最小窗口 / 225% 文字：空状态换行并可滚动访问", emptyScroller.ExtentHeight >= emptyScroller.ViewportHeight && Descendants<TextBlock>(emptyScroller).Where(text => text.IsVisible).All(text => text.TextWrapping == TextWrapping.Wrap));
            Render(window, Path.Combine(root, "empty-high-contrast-225.png"));
        }
        finally { coordinator.SetTheme(ThemePreference.System); }
    }

    private static SolidColorBrush Brush(string key) => (SolidColorBrush)_resourceScope.FindResource(key);
    private static bool Same(Brush actual, SolidColorBrush expected) => actual is SolidColorBrush solid && solid.Color == expected.Color;

    /// <summary>用同字体、同字重的一条探针量单行高度：TextBlock 不公开行盒度量，只能这样取。</summary>
    private static double FirstLineHeight(TextBlock reference)
    {
        var probe = new TextBlock
        {
            Text = "Hg",
            FontFamily = reference.FontFamily,
            FontSize = reference.FontSize,
            FontWeight = reference.FontWeight,
            FontStretch = reference.FontStretch,
            FontStyle = reference.FontStyle,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Height;
    }

    /// <summary>把可能是 alpha 淡染的画刷按底色合成为不透明色；不合成会把 9% 白当成纯白。</summary>
    private static Color Over(Color top, Color bottom)
    {
        if (top.A == 0xFF)
        {
            return top;
        }

        var alpha = top.A / 255d;
        static byte Mix(byte upper, byte lower, double weight) => (byte)Math.Round(upper * weight + lower * (1 - weight));
        return Color.FromRgb(
            Mix(top.R, bottom.R, alpha),
            Mix(top.G, bottom.G, alpha),
            Mix(top.B, bottom.B, alpha));
    }

    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static double Luminance(Color color)
    {
        static double Linear(byte value) { var channel = value / 255d; return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4); }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * 1.5), (int)Math.Ceiling(element.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}

