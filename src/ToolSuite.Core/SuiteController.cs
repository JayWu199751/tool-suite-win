namespace ToolSuite.Core;

/// <summary>托盘、设置和整合检查共用的控制入口。它不拥有 WPF 或旧版全局 Application。</summary>
public sealed class SuiteController
{
    private static readonly IReadOnlyList<(string Id, string Name)> Catalog = ToolIds.Catalog;
    private readonly SuiteSettingsStore store;
    private readonly Dictionary<string, IToolModule> modules;
    private readonly ISuiteAutoStart? autoStart;
    private readonly Dictionary<string, ToolState> states = new();
    private readonly Dictionary<string, string?> errors = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private ThemePreference effectiveTheme = ThemePreference.Light;
    private ThemePreference? appliedTheme;
    public SuiteSettings Settings { get; }
    /// <summary>最近一次开机启动操作的真实失败原因；成功时清空，界面据此如实显示而不伪装已生效。</summary>
    public string? AutoStartError { get; private set; }
    public event Action? Changed;
    /// <summary>主题偏好或有效外观变化时触发；普通命令与启停状态变化不触发，避免无谓重建主题资源。</summary>
    public event Action? AppearanceChanged;
    public event Action<string>? ErrorReported;
    public SuiteController(SuiteSettingsStore store, IEnumerable<IToolModule> modules, ISuiteAutoStart? autoStart = null)
    {
        this.store = store;
        this.modules = modules.ToDictionary(x => x.Id);
        this.autoStart = autoStart;
        Settings = store.Load();
        // 注册表是开机启动的实际状态来源：外部删除或写入失败的残留都不能显示成已生效。
        if (autoStart is not null)
        {
            try { Settings.StartWithWindows = autoStart.IsRegistered; }
            catch (Exception ex)
            {
                Settings.StartWithWindows = false;
                AutoStartError = "无法读取开机启动状态：" + ex.Message;
            }
        }
        foreach (var (id, _) in Catalog) states[id] = this.modules.ContainsKey(id) ? ToolState.Disabled : ToolState.Unavailable;
    }
    public IReadOnlyList<ToolStatus> Tools => Catalog.Select(x => new ToolStatus(x.Id, x.Name,
        Settings.EnabledTools[x.Id], states[x.Id], errors.GetValueOrDefault(x.Id),
        modules.TryGetValue(x.Id, out var module) ? module.Commands : [])).ToArray();
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // 第一次即持久化独立设置，不访问或猜测任何旧目录。
        store.Save(Settings);
        foreach (var (id, module) in modules) module.Configure(CreateContext(id));
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
                // 失败模块可能留有部分启动资源；重试前先尽力清理，避免重复窗口与监听。
                if (states[id] == ToolState.Failed)
                    try { await module.StopAsync(CancellationToken.None); } catch { /* 原始启动/释放错误优先于清理错误。 */ }
                try
                {
                    Directory.CreateDirectory(store.ModuleDirectory(id));
                    module.Configure(CreateContext(id));
                    await module.StartAsync(CreateContext(id), cancellationToken);
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
    /// <summary>统一开机启动开关：先改真实启动项再落盘，任一步失败都回滚并如实返回失败。
    /// 只有注册表确认生效后才把设置记为开启。</summary>
    public bool SetStartWithWindows(bool enabled)
    {
        if (autoStart is null)
        {
            AutoStartError = "当前运行方式无法管理开机启动（需从安装后的正式 EXE 运行）。";
            ReportError(AutoStartError);
            Changed?.Invoke();
            return false;
        }
        var previous = Settings.StartWithWindows;
        try
        {
            autoStart.Apply(enabled);
            if (autoStart.IsRegistered != enabled)
                throw new InvalidOperationException(enabled ? "写入后未找到套件启动项。" : "删除后套件启动项仍然存在。");
        }
        catch (Exception ex)
        {
            // 注册表操作可能在抛出前已部分生效：尽力回到操作前状态，避免界面显示关闭但实际仍会自启。
            try { autoStart.Apply(previous); } catch { /* 原始失败原因优先。 */ }
            Settings.StartWithWindows = previous;
            AutoStartError = ex.Message;
            ReportError(ex.Message);
            Changed?.Invoke();
            return false;
        }
        Settings.StartWithWindows = enabled;
        try { store.Save(Settings); }
        catch (Exception ex)
        {
            // 设置没有保存成功，注册表也不能保留新状态。
            try { autoStart.Apply(previous); } catch { /* 原始保存错误优先。 */ }
            Settings.StartWithWindows = previous;
            AutoStartError = "开机启动设置未保存：" + ex.Message;
            ReportError(AutoStartError);
            Changed?.Invoke();
            return false;
        }
        AutoStartError = null;
        Changed?.Invoke();
        return true;
    }
    public void SetTheme(ThemePreference theme)
    {
        var previous = Settings.Theme;
        Settings.Theme = theme;
        try { store.Save(Settings); }
        catch { Settings.Theme = previous; throw; }
        Changed?.Invoke();
        AppearanceChanged?.Invoke();
    }
    public void ApplyEffectiveTheme(ThemePreference theme)
    {
        // 只在下发给模块的有效外观真正变化时刷新；每个模块的故障隔离，不阻断其他模块。
        if (appliedTheme == theme) return;
        appliedTheme = theme;
        effectiveTheme = theme;
        foreach (var (id, module) in modules)
        {
            try { module.ApplyTheme(theme); }
            catch (Exception ex)
            {
                errors[id] = "主题刷新失败：" + ex.Message;
                ReportError($"{module.DisplayName}主题刷新失败：{ex.Message}");
            }
        }
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
    private ModuleContext CreateContext(string id) => new(store.ModuleDirectory(id), effectiveTheme,
        ReportError, SetTheme, () => Settings.Theme);
    private void ReportError(string message) => ErrorReported?.Invoke(message);
}
