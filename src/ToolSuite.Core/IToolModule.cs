namespace ToolSuite.Core;

/// <summary>适配器构造函数保持轻量；所有窗口、计时器与监听在 StartAsync 创建，在 StopAsync 释放。
/// 这些入口在唯一的 WPF UI 线程调用，异步延续不得丢失同步上下文。
/// PrepareToStopAsync 提交编辑、布局和持久数据，失败直接抛出，不能丢弃窗口。</summary>
public interface IToolModule
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<ToolCommand> Commands { get; }
    Task StartAsync(ModuleContext context, CancellationToken cancellationToken);
    Task PrepareToStopAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    void ApplyTheme(ThemePreference theme);
}

public sealed record ToolCommand(string Id, string Label, Func<CancellationToken, Task> ExecuteAsync,
    bool RequiresRunning = true);

public sealed record ModuleContext(string DataDirectory, ThemePreference Theme, Action<string> ReportError);
public enum ToolState { Unavailable, Disabled, Running, Failed }
public sealed record ToolStatus(string Id, string DisplayName, bool Enabled, ToolState State, string? Error,
    IReadOnlyList<ToolCommand> Commands);
