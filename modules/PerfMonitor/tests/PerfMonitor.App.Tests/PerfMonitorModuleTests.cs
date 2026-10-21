using System.Windows;
using System.IO;
using System.Windows.Threading;
using PerfMonitor.App;
using PerfMonitor.Core.Settings;
using PerfMonitor.Windows.Settings;
using ToolSuite.Core;

namespace PerfMonitor.App.Tests;

public sealed class PerfMonitorModuleTests
{
    [Fact(DisplayName = "模块隐藏保存并停止后，重启恢复隐藏状态且不创建第二个 Application")]
    public Task Hidden_state_survives_module_restart() => OnUiThread(async () =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "isolated-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var module = new PerfMonitorModule();
        try
        {
            new WindowsSettingsStore(Path.Combine(directory, "settings.json")).Save(PerformanceSettings.Default with
            { Metrics = new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false } });
            var application = Application.Current;
            var context = new ModuleContext(directory, ThemePreference.Dark, _ => { });
            await module.StartAsync(context, default);
            var bar = Windows().OfType<PerformanceBarWindow>().Single();
            Assert.True(bar.IsVisible);
            await module.Commands.Single(x => x.Id == "visibility").ExecuteAsync(default);
            Assert.False(bar.IsVisible);
            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);
            Assert.DoesNotContain(Windows(), x => x is PerformanceBarWindow);
            await module.StartAsync(context, default);
            Assert.DoesNotContain(Windows().OfType<PerformanceBarWindow>(), x => x.IsVisible);
            Assert.Equal("显示性能条", module.Commands.Single(x => x.Id == "visibility").Label);
            await module.Commands.Single(x => x.Id == "visibility").ExecuteAsync(default);
            Assert.True(Windows().OfType<PerformanceBarWindow>().Single().IsVisible);
            Assert.Same(application, Application.Current);
            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);
        }
        finally { await module.StopAsync(default); Directory.Delete(directory, true); }
    });

    private static IEnumerable<Window> Windows() => PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>()
        .Select(x => x.RootVisual).OfType<Window>().Where(x => x.Dispatcher == Dispatcher.CurrentDispatcher);

    [Fact(DisplayName = "保存失败保留运行窗口，释放文件锁后可保存停用；隐藏期间时钟不更新")]
    public Task Save_failure_keeps_window_and_hidden_clock_is_paused() => OnUiThread(async () =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "isolated-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var module = new PerfMonitorModule();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            new WindowsSettingsStore(path).Save(PerformanceSettings.Default with
            { Metrics = new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false } });
            await module.StartAsync(new(directory, ThemePreference.Light, _ => { }), default);
            var bar = Windows().OfType<PerformanceBarWindow>().Single();
            using (var fileLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Assert.ThrowsAnyAsync<IOException>(() => module.PrepareToStopAsync(default));
                Assert.True(bar.IsVisible);
            }
            await module.Commands.Single(x => x.Id == "visibility").ExecuteAsync(default);
            var viewModel = Assert.IsType<PerformanceBarViewModel>(bar.DataContext);
            var time = viewModel.TimeText;
            await Task.Delay(1300);
            Assert.Equal(time, viewModel.TimeText);
            await module.PrepareToStopAsync(default);
        }
        finally { await module.StopAsync(default); Directory.Delete(directory, true); }
    });

    [Fact(DisplayName = "冷启动停用时设置可编辑并保存，设置关闭重建且不隐式启用性能条")]
    public Task Offline_settings_save_without_starting_performance_bar() => OnUiThread(async () =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "isolated-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var module = new PerfMonitorModule();
        try
        {
            module.Configure(new(directory, ThemePreference.Dark, _ => { }));
            await module.Commands.Single(x => x.Id == "settings").ExecuteAsync(default);
            var window = Windows().OfType<SettingsWindow>().Single();
            Assert.DoesNotContain(Windows(), x => x is PerformanceBarWindow);
            var gpu = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(window.FindName("GpuToggle"));
            gpu.IsChecked = false;
            window.Close();
            Assert.False(new WindowsSettingsStore(Path.Combine(directory, "settings.json")).Load().Metrics.Gpu);
            await module.Commands.Single(x => x.Id == "settings").ExecuteAsync(default);
            var rebuilt = Windows().OfType<SettingsWindow>().Single();
            Assert.NotSame(window, rebuilt);
            Assert.False(Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(rebuilt.FindName("GpuToggle")).IsChecked);
            module.ApplyTheme(ThemePreference.Light);
            Assert.False(Assert.IsType<System.Windows.Controls.ComboBox>(rebuilt.FindName("ThemeComboBox")).IsVisible);
            Assert.False(Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(rebuilt.FindName("AutostartToggle")).IsVisible);
        }
        finally { await module.StopAsync(default); Directory.Delete(directory, true); }
    });

    internal static Task OnUiThread(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); completion.SetResult(); }
                catch (Exception exception) { completion.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
