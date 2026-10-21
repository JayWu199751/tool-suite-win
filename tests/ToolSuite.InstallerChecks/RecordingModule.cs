using ToolSuite.Core;

namespace ToolSuite.InstallerChecks;

/// <summary>记录登录启动实际启动了哪些工具，不建立真实窗口或监听。</summary>
internal sealed class RecordingModule(string id, string name) : IToolModule
{
    public string Id => id;
    public string DisplayName => name;
    public bool Started { get; private set; }
    public IReadOnlyList<ToolCommand> Commands => [];
    public Task StartAsync(ModuleContext context, CancellationToken cancellationToken) { Started = true; return Task.CompletedTask; }
    public Task PrepareToStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) { Started = false; return Task.CompletedTask; }
    public void ApplyTheme(ThemePreference theme) { }
}
