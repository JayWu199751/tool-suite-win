using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;
using PerfMonitor.Windows.Displays;
using PerfMonitor.Windows.Fullscreen;
using PerfMonitor.Windows.Metrics;
using PerfMonitor.Windows.Shell;

namespace PerfMonitor.App.Shell;

/// <summary>性能条局部窗口端口；只消费宿主 Dispatcher，不拥有 Application、托盘或退出权。</summary>
internal sealed class WpfStartupShellHost : IStartupShellHost, IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly System.Windows.Controls.ContextMenu _menu = new();
    private readonly ShellContextMenuTheme _menuTheme;
    private readonly PerformanceBarViewModel _viewModel = new(listenForSystemTheme: false);
    private readonly WindowsDisplayEnvironmentSource _displays = new();
    private readonly WindowsFullscreenWatcher _fullscreen = new();
    private readonly ISettingsStore _store;
    private readonly Action<string> _reportError;
    private readonly ITaskbarVisibilityGuardPort _guard;
    private PerformanceSettings _settings = PerformanceSettings.Default;
    private Action? _move;
    private Action? _size;
    private PerformanceBarWindow? _bar;
    private SettingsWindow? _settingsWindow;
    private bool _disposed;

    public WpfStartupShellHost(ISettingsStore store, Action<string> reportError)
    {
        _store = store;
        _reportError = reportError;
        _menuTheme = new ShellContextMenuTheme(_menu, false);
        var nativeGuard = new WindowsTaskbarVisibilityGuard(() => !_disposed && _bar is { IsLoaded: true } ? _bar.RootWindowHandle : nint.Zero);
        _guard = new DispatcherGuard(nativeGuard, _dispatcher, () => !_disposed);
    }

    public event EventHandler? ContextMenuRequested;
    public event EventHandler? HideRequested;
    public ISettingsStore SettingsStore => _store;
    public ISystemMetricsSource SystemMetricsSource { get; } = new WindowsSystemMetricsSource();
    public ISlowMetricsSource SlowMetricsSource { get; } = new WindowsSlowMetricsSource();
    public IDisplayEnvironmentSource DisplayEnvironmentSource => _displays;
    public IFullscreenWatcher FullscreenWatcher => _fullscreen;
    public ITaskbarVisibilityGuardPort TaskbarVisibilityGuard => _guard;
    public IPerformanceBarPlacementPort? PerformanceBarPlacement => _bar;
    public PlacementInsets PerformanceBarVisualInsets
    {
        get { var inset = PerformanceBarWindow.ShadowInsetDips * (_bar?.DpiScale ?? 1); return new(inset, inset, inset, inset); }
    }
    public void SetPerformanceBarMoveRequestHandler(Action handler) => _move = handler;
    public void SetPerformanceBarFrameSizeChangedHandler(Action handler) => _size = handler;
    public void BeginPerformanceBarNativeMove() => _bar?.BeginNativeMove();
    public void CreateTrayIcon(Action leftClick, Action rightClick) { }
    public void ShowPerformanceBar(bool activate) => SetPerformanceBarVisible(true, activate);
    public void SetPerformanceBarVisible(bool visible, bool activate)
    {
        EnsureBar();
        _viewModel.SetVisible(visible);
        if (!visible) { _bar!.Hide(); return; }
        _bar!.ShowActivated = activate;
        _bar.Show();
        if (activate) _bar.Activate();
    }
    public void ApplySettings(PerformanceSettings settings)
    {
        _settings = settings;
        _viewModel.ApplySettings(settings);
        _bar?.RefreshNaturalWidth();
        _settingsWindow?.ApplySettings(settings, _store.RecoveredInvalidSettingsOnLastLoad);
        _menuTheme.Apply(settings.Theme == BarTheme.Dark);
    }
    public void SetMetricGeneration(long generation) => _viewModel.SetMetricGeneration(generation);
    public void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_disposed) return;
            _viewModel.Apply(snapshot);
            _bar?.RefreshNaturalWidth();
        }));
    }
    public void ShowContextMenu(ShellMenuOrigin origin, IReadOnlyList<ShellMenuItem> items, Action<ShellMenuAction> selectItem)
    {
        if (_menu.IsOpen || _disposed) return;
        _menu.Items.Clear();
        foreach (var item in items.Where(x => x.Action != ShellMenuAction.Exit))
        {
            if (item.Action == ShellMenuAction.Separator) continue;
            var control = new System.Windows.Controls.MenuItem { Header = item.Label, IsCheckable = item.IsCheckable, IsChecked = item.IsChecked };
            control.Click += (_, _) =>
            {
                try { selectItem(item.Action); }
                catch (Exception exception) { _reportError(exception.Message); }
            };
            _menu.Items.Add(control);
        }
        _menu.PlacementTarget = _bar;
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }
    public void ShowSettingsWindow() => throw new InvalidOperationException("设置窗需要模块配置入口。");
    public void ShowSettingsWindow(PerformanceSettings settings, Func<SettingsPatch, PerformanceSettings> update, bool recovered)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(settings, update, recovered, listenForSystemTheme: false);
            _settingsWindow.Closed += OnSettingsClosed;
        }
        _settingsWindow.ApplySettings(settings, recovered);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
    private void OnSettingsClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window) window.Closed -= OnSettingsClosed;
        _settingsWindow = null;
    }
    public void HideSettingsWindow() => ReleaseSettingsWindow();
    public void ReleaseSettingsWindow()
    {
        var window = _settingsWindow;
        _settingsWindow = null;
        if (window is null) return;
        window.Closed -= OnSettingsClosed;
        window.Close();
    }
    public void Shutdown() => Dispose();
    public void ReportError(string message)
    {
        if (!_disposed && !_dispatcher.HasShutdownStarted)
            _dispatcher.BeginInvoke(new Action(() => { if (!_disposed) _reportError(message); }));
    }
    private void EnsureBar()
    {
        if (_bar is not null) return;
        _bar = new PerformanceBarWindow(() => _move?.Invoke()) { DataContext = _viewModel };
        _bar.ContextMenuRequested += (_, _) => ContextMenuRequested?.Invoke(this, EventArgs.Empty);
        _bar.SizeChanged += (_, _) => _size?.Invoke();
        _bar.Closing += OnBarClosing;
    }
    private void OnBarClosing(object? sender, CancelEventArgs e)
    {
        if (_disposed) return;
        e.Cancel = true;
        HideRequested?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _menu.IsOpen = false;
        _menu.Items.Clear();
        ReleaseSettingsWindow();
        _fullscreen.Dispose();
        _viewModel.Dispose();
        _displays.Dispose();
        if (_bar is { } bar)
        {
            bar.Closing -= OnBarClosing;
            bar.Close();
            bar.DataContext = null;
            _bar = null;
        }
    }
    private sealed class DispatcherGuard(WindowsTaskbarVisibilityGuard inner, Dispatcher dispatcher, Func<bool> active) : ITaskbarVisibilityGuardPort
    {
        private int _fastPending, _slowPending;
        public void EnsureAboveTaskbar()
        {
            if (Interlocked.Exchange(ref _fastPending, 1) != 0 || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _fastPending = 0; if (active()) inner.EnsureAboveTaskbar(); }));
        }
        public void RefreshTaskbarHandles()
        {
            if (Interlocked.Exchange(ref _slowPending, 1) != 0 || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _slowPending = 0; if (active()) inner.RefreshTaskbarHandles(); }));
        }
        public void OnGuardStopped()
        {
            if (dispatcher.CheckAccess()) inner.OnGuardStopped();
            else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(new Action(inner.OnGuardStopped));
        }
    }
}
