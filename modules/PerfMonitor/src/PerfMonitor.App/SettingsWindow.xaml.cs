using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;
using SharedNativeMethods = PerfMonitor.Windows.Native.NativeMethods;
using NativeWindowStyles = PerfMonitor.Windows.Shell.NativeWindowStyles;
using WindowInteropHelper = System.Windows.Interop.WindowInteropHelper;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using Microsoft.Win32;

namespace PerfMonitor.App;

public partial class SettingsWindow : Window
{
    private const double SuiteWindowHeight = 720;
    private bool _usesSuiteLayout;
    private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(2);
    private readonly Func<SettingsPatch, PerformanceSettings> _updateSettings;
    private readonly DispatcherTimer _savedMessageTimer;
    private PerformanceSettings _currentSettings;
    private bool _isSynchronizingControls = true;
    private bool _isListeningForThemeChanges;
    /// <summary>当前有效外观：状态文字的成功/错误色需随明暗取参考稿的两套取值。</summary>
    private bool _isDark;
    private readonly bool _listenForSystemTheme;
    /// <summary>由套件托管时，主题与开机自启只有套件一个权威：两行隐藏，控件不回写这两个字段。</summary>
    private readonly bool _suiteManaged;

    public SettingsWindow(
        PerformanceSettings settings,
        Func<SettingsPatch, PerformanceSettings> updateSettings,
        bool recoveredInvalidSettings, bool listenForSystemTheme = true, bool suiteManaged = true)
    {
        _listenForSystemTheme = listenForSystemTheme;
        _suiteManaged = suiteManaged;
        _currentSettings = settings.Validate();
        _updateSettings = updateSettings;
        InitializeComponent();
        // 主题与自启的可见性只由托管开关决定，不再依赖 XAML 里的固定可见性；分隔线随行一起收起。
        ThemeRow.Visibility = _suiteManaged ? Visibility.Collapsed : Visibility.Visible;
        AutostartRow.Visibility = _suiteManaged ? Visibility.Collapsed : Visibility.Visible;
        ThemeDivider.Visibility = ThemeRow.Visibility;
        AutostartDivider.Visibility = AutostartRow.Visibility;
        _savedMessageTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = SavedMessageDuration
        };
        _savedMessageTimer.Tick += OnSavedMessageTimerTick;
        Closed += (_, _) => _savedMessageTimer.Stop();
        IsVisibleChanged += OnIsVisibleChanged;
        Closed += (_, _) => StopListeningForThemeChanges();
        SourceInitialized += (_, _) => UpdateMaxHeightForCurrentScreen();
        LocationChanged += (_, _) => UpdateMaxHeightForCurrentScreen();
        // ShowInTaskbar=false 的所有权机制不影响 Alt+Tab；Loaded 后补工具窗口样式隐匿任务切换器（ADR 0007）。
        Loaded += (_, _) => NativeWindowStyles.ExcludeFromTaskSwitcher(new WindowInteropHelper(this).Handle);
#if DEBUG
        AutostartToggle.IsEnabled = false;
        AutostartDisabledNote.Visibility = Visibility.Visible;
#endif
        ResynchronizeControls();
        if (recoveredInvalidSettings)
        {
            ShowStatus("配置损坏，已备份原文件并恢复默认设置", isError: true, persistent: true);
        }
    }    public void ApplySettings(PerformanceSettings settings, bool recoveredInvalidSettings)
    {
        _currentSettings = settings.Validate();
        ResynchronizeControls();
        if (recoveredInvalidSettings && string.IsNullOrEmpty(SaveStatusText.Text))
        {
            ShowStatus("配置损坏，已备份原文件并恢复默认设置", isError: true, persistent: true);
        }
    }

    private void MetricChanged(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingControls || sender is not ToggleButton toggle || toggle.Tag is not string metric)
        {
            return;
        }

        var enabled = toggle.IsChecked == true;
        var patch = metric switch
        {
            "Cpu" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Cpu = enabled } },
            "Memory" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Memory = enabled } },
            "Gpu" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Gpu = enabled } },
            "Network" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Network = enabled } },
            "Time" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Time = enabled } },
            _ => null
        };
        if (patch is not null)
        {
            Save(patch);
        }
    }

    private void BehaviorChanged(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingControls || sender is not ToggleButton toggle || toggle.Tag is not string behavior)
        {
            return;
        }

        var enabled = toggle.IsChecked == true;
        // 开机自启由套件统一管理，托管时该控件即使被程序化改动也不回写。
        if (behavior == "Autostart" && _suiteManaged)
        {
            return;
        }

        var patch = behavior switch
        {
            "Autostart" => new SettingsPatch { Autostart = enabled },
            "AutoHideOnFullscreen" => new SettingsPatch { AutoHideOnFullscreen = enabled },
            "CenterInTaskbarRow" => new SettingsPatch { CenterInTaskbarRow = enabled },
            "TransparentDisplay" => new SettingsPatch { TransparentDisplay = enabled },
            _ => null
        };
        if (patch is not null)
        {
            Save(patch);
        }
    }

    private void FastRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingControls && TryGetSelectedInteger(FastRefreshComboBox, out var milliseconds))
        {
            Save(new SettingsPatch { FastRefreshMilliseconds = milliseconds });
        }
    }

    private void SlowRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingControls && TryGetSelectedInteger(SlowRefreshComboBox, out var milliseconds))
        {
            Save(new SettingsPatch { SlowRefreshMilliseconds = milliseconds });
        }
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suiteManaged)
        {
            return;
        }

        if (!_isSynchronizingControls && ThemeComboBox.SelectedValue is string themeName
            && Enum.TryParse<BarTheme>(themeName, ignoreCase: false, out var theme))
        {
            Save(new SettingsPatch { Theme = theme });
        }
    }

    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSynchronizingControls || OpacityValueText is null)
        {
            return;
        }

        var steppedOpacity = PerformanceSettings.SnapOpacity(OpacitySlider.Value);
        if (Math.Abs(OpacitySlider.Value - steppedOpacity) > 0.0001)
        {
            _isSynchronizingControls = true;
            OpacitySlider.Value = steppedOpacity;
            _isSynchronizingControls = false;
        }

        OpacityValueText.Text = $"{steppedOpacity.ToString("0%", CultureInfo.InvariantCulture)}";
        Save(new SettingsPatch { Opacity = steppedOpacity });
    }

    private void FontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isSynchronizingControls && FontSizeValueText is not null)
        {
            var fontSize = (int)Math.Round(FontSizeSlider.Value, MidpointRounding.AwayFromZero);
            FontSizeValueText.Text = fontSize.ToString(CultureInfo.InvariantCulture);
            Save(new SettingsPatch { FontSize = fontSize });
        }
    }

    private void Save(SettingsPatch patch)
    {
        try
        {
            _currentSettings = _updateSettings(patch);
            ResynchronizeControls();
            ShowStatus("已保存", isError: false, persistent: false);
        }
        catch (Exception exception)
        {
            ResynchronizeControls();
            var reason = exception is System.IO.IOException or UnauthorizedAccessException
                ? "无法写入配置文件，请检查文件占用或访问权限。" : exception.Message;
            ShowStatus("保存失败：" + reason, isError: true, persistent: true);
            System.Diagnostics.Trace.WriteLine($"保存设置失败：{exception}");
        }
    }

    /// <summary>按最新设置重挂控件与主题；期间抑制控件事件回写。</summary>
    private void ResynchronizeControls()
    {
        _isSynchronizingControls = true;
        SynchronizeControls(_currentSettings);
        ApplyTheme(_currentSettings.Theme);
        _isSynchronizingControls = false;
    }

    private void SynchronizeControls(PerformanceSettings settings)
    {
        CpuToggle.IsChecked = settings.Metrics.Cpu;
        MemoryToggle.IsChecked = settings.Metrics.Memory;
        GpuToggle.IsChecked = settings.Metrics.Gpu;
        NetworkToggle.IsChecked = settings.Metrics.Network;
        TimeToggle.IsChecked = settings.Metrics.Time;
        AutostartToggle.IsChecked = settings.Autostart;
        FullscreenAutoHideToggle.IsChecked = settings.AutoHideOnFullscreen;
        CenterInTaskbarRowToggle.IsChecked = settings.CenterInTaskbarRow;
        TransparentDisplayToggle.IsChecked = settings.TransparentDisplay;
        FastRefreshComboBox.SelectedValue = settings.FastRefreshMilliseconds.ToString(CultureInfo.InvariantCulture);
        SlowRefreshComboBox.SelectedValue = settings.SlowRefreshMilliseconds.ToString(CultureInfo.InvariantCulture);
        ThemeComboBox.SelectedValue = settings.Theme.ToString();
        OpacitySlider.Value = settings.Opacity;
        OpacityValueText.Text = settings.Opacity.ToString("0%", CultureInfo.InvariantCulture);
        FontSizeSlider.Value = settings.FontSize;
        FontSizeValueText.Text = settings.FontSize.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyTheme(BarTheme theme)
    {
        var dark = PerformanceBarViewModel.IsDarkEffectiveTheme(theme);
        _isDark = dark;
        // 取值见 docs/UI Design/Apple Dark.md（暗）与 Apple Light.md（亮）：
        // 近黑/浅灰画布 + 炭黑/白卡片；描边、分隔与悬停沿用参考稿的 alpha 白；强调只用一个色族。
        Resources["PageBackgroundBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x0B, 0x0B, 0x0E) : Color.FromRgb(0xF5, 0xF5, 0xF7));
        Resources["SurfaceBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x14, 0x14, 0x19) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        Resources["PrimaryTextBrush"] = CreateBrush(
            dark ? Color.FromRgb(0xF2, 0xF2, 0xF5) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        Resources["SecondaryTextBrush"] = CreateBrush(
            dark ? Color.FromRgb(0xA0, 0xA0, 0xAC) : Color.FromRgb(0x63, 0x63, 0x6A));
        Resources["AccentBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x7B, 0x77, 0xE0) : Color.FromRgb(0x00, 0x71, 0xE3));
        Resources["ControlBackgroundBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x15, 0x15, 0x1A) : Color.FromRgb(0xF5, 0xF5, 0xF7));
        Resources["SwitchTrackBrush"] = CreateBrush(
            dark ? Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0xE8, 0xE8, 0xED));
        Resources["DividerBrush"] = CreateBrush(
            dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0xE8, 0xE8, 0xED));
        Resources["HoverBrush"] = CreateBrush(
            dark ? Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0xF5, 0xF5, 0xF7));
        Resources["ThumbBrush"] = CreateBrush(
            dark ? Color.FromRgb(0xF2, 0xF2, 0xF5) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "PageBackgroundBrush", "SurfaceBrush", "ControlBackgroundBrush", "ThumbBrush" })
                Resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "PrimaryTextBrush", "SecondaryTextBrush", "DividerBrush" })
                Resources[key] = SystemColors.WindowTextBrush;
            Resources["AccentBrush"] = SystemColors.HighlightBrush;
            Resources["SwitchTrackBrush"] = SystemColors.ControlDarkBrush;
            Resources["HoverBrush"] = SystemColors.ControlBrush;
        }
    }

    /// <summary>
    /// 按窗口当前所在显示器的工作区重新约束最大高度（DIP）；
    /// 跨屏移动、工作区变化时重新计算。统一设置固定高度，超出即由内容区滚动。
    /// </summary>
    private void UpdateMaxHeightForCurrentScreen()
    {
        var workAreaHeight = TryGetMonitorWorkAreaHeight(out var monitorWorkAreaHeight)
            ? monitorWorkAreaHeight
            : SystemParameters.WorkArea.Height;
        var maximum = SettingsWindowSizingRules.ResolveMaxWindowHeight(workAreaHeight);
        if (_usesSuiteLayout)
        {
            var height = Math.Min(SuiteWindowHeight, maximum);
            // 先放宽旧下限再调整上限，兼容从大屏移到较小工作区。
            MinHeight = Math.Min(MinHeight, height);
            MaxHeight = height;
            MinHeight = height;
            Height = height;
        }
        else MaxHeight = maximum;
    }

    /// <summary>读取窗口所在显示器工作区高度并换算为 DIP；失败时返回 false 走主屏回退。</summary>
    private bool TryGetMonitorWorkAreaHeight(out double workAreaHeight)
    {
        workAreaHeight = 0;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return false;
        }

        try
        {
            var monitor = SharedNativeMethods.MonitorFromWindow(
                handle, SharedNativeMethods.MonitorDefaultToNearest);
            if (monitor == nint.Zero)
            {
                return false;
            }

            var info = new SharedNativeMethods.MonitorInfoW
            {
                CbSize = (uint)Marshal.SizeOf<SharedNativeMethods.MonitorInfoW>()
            };
            if (!SharedNativeMethods.GetMonitorInfoW(monitor, ref info))
            {
                return false;
            }

            var dpiScale = 1.0;
            try
            {
                if (SharedNativeMethods.GetDpiForMonitor(
                        monitor, SharedNativeMethods.DpiEffective, out var dpiX, out _) == 0
                    && dpiX > 0)
                {
                    dpiScale = dpiX / 96.0;
                }
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or EntryPointNotFoundException)
            {
            }

            if (dpiScale <= 0)
            {
                return false;
            }

            workAreaHeight = (info.RcWork.Bottom - info.RcWork.Top) / dpiScale;
            return workAreaHeight > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ShowStatus(string message, bool isError, bool persistent)
    {
        _savedMessageTimer.Stop();
        SaveStatusText.Text = message;
        // 状态色两套取值：暗色用 #57C08A / #E05A52，亮色用 #1D8348 / #C7261F（均为参考稿的原值）。
        SaveStatusText.Foreground = CreateBrush(isError
            ? (_isDark ? Color.FromRgb(0xE0, 0x5A, 0x52) : Color.FromRgb(0xC7, 0x26, 0x1F))
            : (_isDark ? Color.FromRgb(0x57, 0xC0, 0x8A) : Color.FromRgb(0x1D, 0x83, 0x48)));
        if (SystemParameters.HighContrast) SaveStatusText.Foreground = SystemColors.WindowTextBrush;
        if (!persistent)
        {
            _savedMessageTimer.Start();
        }
    }

    private void OnSavedMessageTimerTick(object? sender, EventArgs e)
    {
        _savedMessageTimer.Stop();
        SaveStatusText.Text = string.Empty;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartListeningForThemeChanges();
        }
        else
        {
            StopListeningForThemeChanges();
        }
    }

    private void StartListeningForThemeChanges()
    {
        if (_isListeningForThemeChanges)
        {
            return;
        }

        _isListeningForThemeChanges = true;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        if (_listenForSystemTheme) SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void StopListeningForThemeChanges()
    {
        if (!_isListeningForThemeChanges)
        {
            return;
        }

        _isListeningForThemeChanges = false;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        if (_listenForSystemTheme) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            UpdateMaxHeightForCurrentScreen();
        }

        if (e.PropertyName == nameof(SystemParameters.HighContrast) || _currentSettings.Theme == BarTheme.System)
        {
            ApplyTheme(_currentSettings.Theme);
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_currentSettings.Theme == BarTheme.System
            && !Dispatcher.HasShutdownStarted
            && !Dispatcher.HasShutdownFinished)
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ApplyTheme(BarTheme.System)));
        }
    }

    private static bool TryGetSelectedInteger(ComboBox comboBox, out int value)
    {
        if (comboBox.SelectedValue is not null
            && int.TryParse(Convert.ToString(comboBox.SelectedValue, CultureInfo.InvariantCulture), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>头部标题行即拖动把手；关闭按钮自身吃掉鼠标按下事件，不会误触发拖动。</summary>
    private void HeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>宿主在原窗口内重排已有控件，事件、保存反馈和 DPI 约束仍由此窗口维护。</summary>
    public StackPanel DetachSettingsContent()
    {
        SettingsScroll.Content = null;
        SettingsBody.Margin = new Thickness(0);
        foreach (var card in SettingsBody.Children.OfType<Border>())
        {
            card.Padding = new Thickness(0);
            card.Background = null;
            card.Margin = new Thickness(0, 0, 0, 16);
        }
        return SettingsBody;
    }

    public void UseSuiteContent(UIElement content)
    {
        _usesSuiteLayout = true;
        SizeToContent = SizeToContent.Manual;
        Title = "工具套件设置";
        WindowHeading.Text = "工具套件";
        WindowDescription.Text = "通用偏好与三个工具的设置";
        Width = 380;
        MinWidth = 380;
        MaxWidth = 380;
        ResizeMode = ResizeMode.NoResize;
        SettingsScroll.Content = content;
        UpdateMaxHeightForCurrentScreen();
    }
}
