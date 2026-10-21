using ToolSuite.Core;

var root = Path.GetFullPath(args[0]);
var store = new SuiteSettingsStore(root);
var initial = store.Load();
Assert(initial.Theme == ThemePreference.System && !initial.StartWithWindows && initial.EnabledTools.Values.All(x => x), "首次主题、启停与自启偏好");
Assert(store.ModuleDirectory("clipboard").StartsWith(root + Path.DirectorySeparatorChar), "模块数据独立于原应用");
initial.Theme = ThemePreference.Dark;
initial.EnabledTools["pinto"] = false;
store.Save(initial);
var restored = new SuiteSettingsStore(root).Load();
Assert(restored.Theme == ThemePreference.Dark && !restored.EnabledTools["pinto"], "配置重启恢复");
Console.WriteLine("PASS: 隔离数据、首次偏好和保存恢复");

var module = new DraftTool();
var controller = new SuiteController(new SuiteSettingsStore(Path.Combine(root, "lifecycle")), [module]);
await controller.StartAsync();
Assert(controller.Tools.Single(t => t.Id == "clipboard").State == ToolState.Running, "真实模块成功启动才显示运行");
Assert(controller.Tools.Single(t => t.Id == "pinto").State == ToolState.Unavailable, "未接入模块不可用");
module.RefuseSave = true;
Assert(!await controller.SetEnabledAsync("clipboard", false), "保存失败拒绝停用");
Assert(controller.Tools.Single(t => t.Id == "clipboard").State == ToolState.Running, "失败保留运行状态");
Assert(!await controller.PrepareExitAsync(), "保存失败拒绝退出");
module.RefuseSave = false;
Assert(await controller.SetEnabledAsync("clipboard", false), "保存成功后停用");
Assert(await controller.SetEnabledAsync("clipboard", true), "重新启用重建模块");
Assert(await controller.PrepareExitAsync(), "全部保存后可退出");
await controller.StopAsync();
Console.WriteLine("PASS: 模块真实启停、不可用状态、失败保留及安全退出");

var failingRoot = Path.Combine(root, "save-failure");
var failingStore = new SuiteSettingsStore(failingRoot);
var disabled = new SuiteSettings();
disabled.EnabledTools["clipboard"] = false;
failingStore.Save(disabled);
var startingTool = new DraftTool();
var failingController = new SuiteController(failingStore, [startingTool]);
await failingController.StartAsync();
// 真实文件系统拒绝设置目标：模块可以启动，但无法把启用偏好写入设置。
Directory.CreateDirectory(Path.Combine(failingRoot, "suite.json.tmp"));
Assert(!await failingController.SetEnabledAsync("clipboard", true), "偏好写入失败必须报告失败");
Assert(failingController.Tools.Single(t => t.Id == "clipboard").State != ToolState.Running,
    "原为停用时启用落盘失败，必须回滚刚启动的模块");
Assert(!failingStore.Load().EnabledTools["clipboard"], "失败后落盘仍为停用");
Console.WriteLine("PASS: 启用落盘失败回滚运行资源");

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

sealed class DraftTool : IToolModule
{
    public string Id => "clipboard";
    public string DisplayName => "测试编辑器";
    public bool RefuseSave { get; set; }
    public IReadOnlyList<ToolCommand> Commands => [];
    public Task StartAsync(ModuleContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PrepareToStopAsync(CancellationToken cancellationToken)
        => RefuseSave ? Task.FromException(new IOException("模拟存储边界拒绝保存")) : Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public void ApplyTheme(ThemePreference theme) { }
}
