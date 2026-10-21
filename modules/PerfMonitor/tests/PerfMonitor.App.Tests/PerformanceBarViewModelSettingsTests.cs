using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PerfMonitor.App;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.App.Tests;

public sealed class PerformanceBarViewModelSettingsTests
{
    [Fact(DisplayName = "透明显示开合按设置更新边框粗细")]
    public void Toggling_transparent_display_updates_border_thickness()
    {
        var vm = new PerformanceBarViewModel();
        try
        {
            vm.ApplySettings(PerformanceSettings.Default with { TransparentDisplay = true });
            var transparentThickness = vm.BorderThickness;

            vm.ApplySettings(PerformanceSettings.Default with { TransparentDisplay = false });

            Assert.Equal(new Thickness(0), transparentThickness);
            Assert.Equal(new Thickness(1), vm.BorderThickness);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact(DisplayName = "字号变化更新字号与派生间距属性")]
    public void Changing_font_size_updates_font_size_properties()
    {
        var vm = new PerformanceBarViewModel();
        try
        {
            var before = vm.FontSize;

            vm.ApplySettings(PerformanceSettings.Default with { FontSize = 14 });

            Assert.Equal(14, vm.FontSize);
            Assert.NotEqual(before, vm.FontSize);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact(DisplayName = "深色主题主占用读数与时间同色，缺失回落弱化色，卡片零投影")]
    public void Dark_theme_colors_readings_with_preview_tokens()
    {
        var vm = new PerformanceBarViewModel();
        try
        {
            vm.ApplySettings(PerformanceSettings.Default with { Theme = BarTheme.Dark });

            var label = Assert.IsType<SolidColorBrush>(vm.LabelBrush);
            Assert.Equal(Color.FromRgb(0x86, 0x86, 0x8B), label.Color);
            // 视觉规范要求零投影：层级只靠色差与圆角，卡片不挂任何 Effect。
            Assert.Null(vm.ShadowEffect);

            vm.SetMetricGeneration(1);
            vm.Apply(new PerformanceMetricsSnapshot(
                1, CpuPercentage: 9, MemoryPercentage: 51, MemoryUsedGiB: null, MemoryTotalGiB: null,
                DateTimeOffset.UtcNow));

            Assert.Equal(Color.FromRgb(0xF5, 0xF5, 0xF7), ((SolidColorBrush)vm.ForegroundBrush).Color);
            Assert.Equal(vm.ForegroundBrush, vm.CpuPercentageBrush);
            Assert.Equal(vm.ForegroundBrush, vm.MemoryPercentageBrush);
            // GPU 读数未到达：缺失占位用弱化色，而非零值或主题色。
            Assert.Equal(vm.MissingBrush, vm.GpuPercentageBrush);

            vm.Apply(new PerformanceMetricsSnapshot(
                1, CpuPercentage: 9, MemoryPercentage: 51, MemoryUsedGiB: null, MemoryTotalGiB: null,
                DateTimeOffset.UtcNow, GpuPercentage: 42));
            Assert.Equal(vm.ForegroundBrush, vm.GpuPercentageBrush);

            vm.ApplySettings(PerformanceSettings.Default with { Theme = BarTheme.Light });
            Assert.Equal(Color.FromRgb(0x1D, 0x1D, 0x1F), ((SolidColorBrush)vm.ForegroundBrush).Color);
            Assert.Equal(vm.ForegroundBrush, vm.CpuPercentageBrush);
            Assert.Equal(vm.ForegroundBrush, vm.MemoryPercentageBrush);
            Assert.Equal(vm.ForegroundBrush, vm.GpuPercentageBrush);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact(DisplayName = "亮色主题主占用读数与时间同色，网络箭头使用中性层次")]
    public void Light_theme_uses_derived_tokens()
    {
        var vm = new PerformanceBarViewModel();
        try
        {
            vm.ApplySettings(PerformanceSettings.Default with { Theme = BarTheme.Light });

            var background = Assert.IsType<SolidColorBrush>(vm.BackgroundBrush);
            Assert.Equal(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF), background.Color);

            vm.SetMetricGeneration(1);
            vm.Apply(new PerformanceMetricsSnapshot(
                1, CpuPercentage: 9, MemoryPercentage: 51, MemoryUsedGiB: null, MemoryTotalGiB: null,
                DateTimeOffset.UtcNow, GpuPercentage: 42, NetworkUploadMegabytesPerSecond: 1.2));

            Assert.Equal(Color.FromRgb(0x1D, 0x1D, 0x1F), ((SolidColorBrush)vm.ForegroundBrush).Color);
            Assert.Equal(vm.ForegroundBrush, vm.CpuPercentageBrush);
            Assert.Equal(vm.ForegroundBrush, vm.MemoryPercentageBrush);
            Assert.Equal(vm.ForegroundBrush, vm.GpuPercentageBrush);
            Assert.Equal(Color.FromRgb(0x63, 0x63, 0x6A), ((SolidColorBrush)vm.NetworkUploadArrowBrush).Color);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact(DisplayName = "透明显示隐藏装饰并保留卡片命中区")]
    public void Transparent_display_hides_decorations_but_keeps_the_hit_area()
    {
        var vm = new PerformanceBarViewModel();
        try
        {
            vm.ApplySettings(PerformanceSettings.Default with
            {
                Theme = BarTheme.Dark,
                TransparentDisplay = true
            });

            var background = Assert.IsType<SolidColorBrush>(vm.BackgroundBrush);
            Assert.Equal(1, background.Color.A);
            Assert.Null(vm.ShadowEffect);
            Assert.Equal(0, ((SolidColorBrush)vm.DividerBrush).Color.A);
            Assert.Equal(0, ((SolidColorBrush)vm.HoverBrush).Color.A);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact(DisplayName = "默认背景不透明度为 0.96 且合法")]
    public void Default_opacity_is_preview_aligned()
    {
        Assert.Equal(0.96, PerformanceSettings.Default.Opacity);
        Assert.Equal(0.96, PerformanceSettings.Default.Validate().Opacity);
    }
}
