using System.IO;
using ToolSuite.Core;

namespace ToolSuite.SuiteChecks;

/// <summary>通过 SuiteController 公开入口检查控制语义：主题只在有效外观变化时下发、
/// 单模块主题故障隔离、失败模块重试前清理残留。</summary>
internal static class ControllerSemanticsChecks
{
    internal static async Task RunAsync(string root, List<string> log)
    {
        await AppearanceOnlyRefreshAsync(Path.Combine(root, "appearance"), log);
        await ThemeFailureIsolationAsync(Path.Combine(root, "theme-isolation"), log);
        await RetryCleansResidualAsync(Path.Combine(root, "retry"), log);
    }

    private static async Task AppearanceOnlyRefreshAsync(string root, List<string> log)
    {
        var spy = new SpyModule("clipboard");
        var controller = new SuiteController(new SuiteSettingsStore(root), [spy]);
        await controller.StartAsync();
        var afterStart = spy.Themes.Count;
        await controller.ExecuteCommandAsync("clipboard", "ping");
        await controller.SetEnabledAsync("clipboard", false);
        await controller.SetEnabledAsync("clipboard", true);
        Assert(spy.Themes.Count == afterStart, "普通命令与启停状态变化不得重建主题");
        controller.ApplyEffectiveTheme(ThemePreference.Dark);
        Assert(spy.Themes.Count == afterStart + 1 && spy.Themes[^1] == ThemePreference.Dark,
            "有效外观变化必须下发一次主题");
        controller.ApplyEffectiveTheme(ThemePreference.Dark);
        Assert(spy.Themes.Count == afterStart + 1, "同一有效外观不得重复分配主题");
        await controller.StopAsync();
        log.Add("普通命令/启停不刷新主题；有效外观变化仅下发一次");
    }

    private static async Task ThemeFailureIsolationAsync(string root, List<string> log)
    {
        var bad = new SpyModule("clipboard") { ThrowOnTheme = true };
        var good = new SpyModule("perf-monitor");
        var controller = new SuiteController(new SuiteSettingsStore(root), [bad, good]);
        var errors = new List<string>();
        controller.ErrorReported += errors.Add;
        await controller.StartAsync();
        controller.ApplyEffectiveTheme(ThemePreference.Dark);
        Assert(good.Themes.Count == 1 && good.Themes[0] == ThemePreference.Dark,
            "单模块主题异常不得阻断其他模块的主题刷新");
        Assert(errors.Count == 1 && errors[0].Contains("主题"), "主题异常必须显式报告");
        Assert(controller.Tools.Single(x => x.Id == "perf-monitor").State == ToolState.Running,
            "主题异常不得伪装模块停止");
        await controller.StopAsync();
        log.Add("单模块 ApplyTheme 异常被隔离并报告，其他模块继续接收主题");
    }

    private static async Task RetryCleansResidualAsync(string root, List<string> log)
    {
        var flaky = new SpyModule("clipboard") { FailStop = true };
        var controller = new SuiteController(new SuiteSettingsStore(root), [flaky]);
        await controller.StartAsync();
        Assert(!await controller.SetEnabledAsync("clipboard", false), "释放失败必须报告停用失败");
        Assert(controller.Tools.Single(x => x.Id == "clipboard").State == ToolState.Failed,
            "释放失败不得伪装为已停用");
        flaky.FailStop = false;
        Assert(await controller.SetEnabledAsync("clipboard", true), "失败后允许重试启用");
        var stops = flaky.Calls.Count(call => call == "stop");
        Assert(stops == 2, $"重试启动前必须清理残留释放（实际 stop 次数 {stops}）");
        Assert(flaky.Calls[^1] == "start" && flaky.Calls[^2] == "stop", "残留清理必须发生在重试启动之前");
        await controller.StopAsync();
        log.Add("失败模块重试前先清理残留，不伪装停用");
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    /// <summary>公开契约的测试替身：只记录宿主经公开入口发起的调用。</summary>
    private sealed class SpyModule(string id) : IToolModule
    {
        public string Id => id;
        public string DisplayName => id;
        public List<string> Calls { get; } = [];
        public List<ThemePreference> Themes { get; } = [];
        public bool ThrowOnTheme { get; init; }
        public bool FailStop { get; set; }
        public IReadOnlyList<ToolCommand> Commands =>
            [new("ping", "Ping", _ => { Calls.Add("command"); return Task.CompletedTask; })];
        public void Configure(ModuleContext context) { }
        public Task StartAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            Calls.Add("start");
            return Task.CompletedTask;
        }
        public Task PrepareToStopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("prepare");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("stop");
            return FailStop ? Task.FromException(new IOException("模拟释放失败")) : Task.CompletedTask;
        }
        public void ApplyTheme(ThemePreference theme)
        {
            Themes.Add(theme);
            if (ThrowOnTheme) throw new InvalidOperationException("模拟主题刷新故障");
        }
    }
}
