using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

public sealed class MetricDemandContractTests
{
    [Fact(DisplayName = "仅显示时间时不查询 CPU、内存、网络、GPU 或 ACPI")]
    public async Task Time_only_does_not_query_hardware()
    {
        var host = new DemandHost(new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false });
        using var controller = new StartupShellController(host);
        controller.Start();
        await Task.Delay(200);
        Assert.Equal(0, host.CpuReads + host.MemoryReads + host.NetworkReads + host.GpuReads + host.TemperatureReads);
    }

    [Fact(DisplayName = "仅 GPU 时不查询快指标和 ACPI，关闭全部指标后停止 GPU 查询")]
    public async Task Gpu_only_uses_only_gpu_and_all_disabled_stops_queries()
    {
        var host = new DemandHost(new MetricVisibility { Cpu = false, Memory = false, Network = false, Time = false });
        using var controller = new StartupShellController(host);
        controller.Start();
        await Task.Delay(200);
        Assert.True(host.GpuReads > 0);
        Assert.Equal(0, host.CpuReads + host.MemoryReads + host.NetworkReads + host.TemperatureReads);
        controller.UpdateSettings(new SettingsPatch { Metrics = new MetricsSettingsPatch { Gpu = false } });
        var reads = host.GpuReads;
        await Task.Delay(200);
        Assert.Equal(reads, host.GpuReads);
        await controller.StopAsync();
    }

    [Fact(DisplayName = "仅内存时不查询 CPU、网络和慢指标")]
    public async Task Memory_only_uses_only_memory()
    {
        var host = new DemandHost(new MetricVisibility { Cpu = false, Gpu = false, Network = false, Time = false });
        using var controller = new StartupShellController(host);
        controller.Start();
        await Task.Delay(200);
        Assert.True(host.MemoryReads > 0);
        Assert.Equal(0, host.CpuReads + host.NetworkReads + host.GpuReads + host.TemperatureReads);
        await controller.StopAsync();
    }

    internal sealed class DemandHost(MetricVisibility metrics) : IStartupShellHost, ISettingsStore, ISystemMetricsSource, ISlowMetricsSource
    {
        public int CpuReads, MemoryReads, NetworkReads, GpuReads, TemperatureReads;
        public ISettingsStore SettingsStore => this;
        public ISystemMetricsSource SystemMetricsSource => this;
        public ISlowMetricsSource SlowMetricsSource => this;
        public PerformanceSettings Load() => PerformanceSettings.Default with { Metrics = metrics };
        public void Save(PerformanceSettings settings) { }
        public CpuTimeCounters? ReadCpuTimes() { Interlocked.Increment(ref CpuReads); return new(100, 10, 50); }
        public PhysicalMemoryCounters? ReadPhysicalMemory() { Interlocked.Increment(ref MemoryReads); return new(1000, 500); }
        public NetworkCountersSnapshot? ReadNetworkCounters() { Interlocked.Increment(ref NetworkReads); return new(TimeSpan.FromSeconds(1), []); }
        public Task<GpuMetricsReading?> ReadGpuMetricsAsync(CancellationToken token) { Interlocked.Increment(ref GpuReads); return Task.FromResult<GpuMetricsReading?>(new(25, 30, 40)); }
        public Task<int?> ReadCpuTemperatureCelsiusAsync(CancellationToken token) { Interlocked.Increment(ref TemperatureReads); return Task.FromResult<int?>(45); }
        public void SetPerformanceBarMoveRequestHandler(Action handler) { }
        public void BeginPerformanceBarNativeMove() { }
        public void ShowPerformanceBar(bool activate) { }
        public void SetPerformanceBarVisible(bool visible, bool activate) { }
        public void CreateTrayIcon(Action leftClick, Action rightClick) { }
        public void ShowContextMenu(ShellMenuOrigin origin, IReadOnlyList<ShellMenuItem> items, Action<ShellMenuAction> selectItem) { }
        public void ShowSettingsWindow() { }
        public void HideSettingsWindow() { }
        public void SetMetricGeneration(long generation) { }
        public void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot) { }
        public void Shutdown() { }
    }
}
