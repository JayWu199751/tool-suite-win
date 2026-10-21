namespace ToolSuite.Core;

/// <summary>托盘、设置和整合检查共用的控制入口。它不拥有 WPF 或旧版全局 Application。</summary>
public sealed class SuiteController
{
    private static readonly (string Id, string Name)[] Catalog =
        [("clipboard", "剪贴板"), ("perf-monitor", "性能条"), ("pinto", "便签")];
    private readonly SuiteSettingsStore store;
    private readonly Dictionary<string, IToolModule> modules;
    private readonly Dictionary<string, ToolState> states = new();
    private readonly Dictionary<string, string?> errors = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private ThemePreference effectiveTheme = ThemePreference.Light;
    public SuiteSettings Settings { get; }
    public event Action? Changed;
    public event Action<string>? ErrorReported;
    public SuiteController(SuiteSettingsStore store, IEnumerable<IToolModule> modules)
    {
        this.store = store;
        this.modules = modules.ToDictionary(x => x.Id);
        Settings = store.Load();
        foreach (var (id, _) in Catalog) states[id] = this.modules.ContainsKey(id) ? ToolState.Disabled : ToolState.Unavailable;
    }
    public IReadOnlyList<ToolStatus> Tools => Catalog.Select(x => new ToolStatus(x.Id, x.Name,
        Settings.EnabledTools[x.Id], states[x.Id], errors.GetValueOrDefault(x.Id),
        modules.TryGetValue(x.Id, out var module) ? module.Commands : [])).ToArray();
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // 第一次即持久化独立设置，不访问或猜测任何旧目录。
        store.Save(Settings);
        foreach (var (id, _) in Catalog)
            if (Settings.EnabledTools[id] && modules.ContainsKey(id)) await SetEnabledAsync(id, true, cancellationToken);
    }
    public async Task<bool> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!modules.TryGetValue(id, out var module)) return false;
            var startedHere = false;
            if (enabled && states[id] != ToolState.Running)
            {
                try
                {
                    Directory.CreateDirectory(store.ModuleDirectory(id));
                    await module.StartAsync(new(store.ModuleDirectory(id), effectiveTheme, ReportError), cancellationToken);
                    states[id] = ToolState.Running;
                    startedHere = true;
                }
                catch (Exception ex)
                {
                    try { await module.StopAsync(CancellationToken.None); } catch { /* 原始启动错误优先。 */ }
                    states[id] = ToolState.Failed;
                    throw new InvalidOperationException($"{module.DisplayName}启动失败：{ex.Message}", ex);
                }
            }
            if (!enabled && states[id] == ToolState.Running) await module.PrepareToStopAsync(cancellationToken);
            var previous = Settings.EnabledTools[id];
            Settings.EnabledTools[id] = enabled;
            try { store.Save(Settings); }
            catch
            {
                Settings.EnabledTools[id] = previous;
                if (startedHere && !previous)
                {
                    try { await module.StopAsync(CancellationToken.None); states[id] = ToolState.Disabled; }
                    catch { states[id] = ToolState.Failed; throw; }
                }
                throw;
            }
            if (!enabled)
            {
                try { await module.StopAsync(cancellationToken); states[id] = ToolState.Disabled; }
                catch { states[id] = ToolState.Failed; Settings.EnabledTools[id] = previous; store.Save(Settings); throw; }
            }
            errors[id] = null;
            return true;
        }
        catch (Exception ex) { errors[id] = ex.Message; ReportError(ex.Message); return false; }
        finally { gate.Release(); Changed?.Invoke(); }
    }
    public async Task<bool> ExecuteCommandAsync(string id, string commandId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!modules.TryGetValue(id, out var module)) return false;
            var command = module.Commands.Single(x => x.Id == commandId);
            if (command.RequiresRunning && states[id] != ToolState.Running) return false;
            await command.ExecuteAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) { ReportError(ex.Message); return false; }
        finally { gate.Release(); Changed?.Invoke(); }
    }
    public void SetTheme(ThemePreference theme)
    {
        var previous = Settings.Theme;
        Settings.Theme = theme;
        try { store.Save(Settings); }
        catch { Settings.Theme = previous; throw; }
        Changed?.Invoke();
    }
    public void ApplyEffectiveTheme(ThemePreference theme)
    {
        effectiveTheme = theme;
        foreach (var (id, module) in modules)
            if (states[id] == ToolState.Running) module.ApplyTheme(theme);
    }
    public async Task<bool> PrepareExitAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var (id, module) in modules)
                if (states[id] == ToolState.Running) await module.PrepareToStopAsync(cancellationToken);
            store.Save(Settings);
            return true;
        }
        catch (Exception ex) { ReportError(ex.Message); return false; }
        finally { gate.Release(); }
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            // 一个模块释放失败也继续释放其他模块，然后阻止宿主伪装正常退出。
            List<Exception> failures = [];
            foreach (var (id, module) in modules)
                if (states[id] is ToolState.Running or ToolState.Failed)
                    try { await module.StopAsync(cancellationToken); states[id] = ToolState.Disabled; }
                    catch (Exception ex) { failures.Add(ex); }
            if (failures.Count > 0) throw new AggregateException("部分工具未能释放资源。", failures);
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }
    private void ReportError(string message) => ErrorReported?.Invoke(message);
}
