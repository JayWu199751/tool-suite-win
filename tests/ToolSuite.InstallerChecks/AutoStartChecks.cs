using System.IO;
using System.Windows;
using ToolSuite.App;
using ToolSuite.Core;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Application = System.Windows.Application;

namespace ToolSuite.InstallerChecks;

/// <summary>统一开机启动的隔离检查：可控 Run 存储，不接触真实 HKCU Run 或原三个应用的启动项。</summary>
internal static class AutoStartChecks
{
    private const string InstalledExe = @"C:\Program Files\ToolSuite\ToolSuite.exe";

    internal static void Run(string root, List<string> log)
    {
        DefaultOff(root, log);
        EnableWritesOnlySuiteValue(root, log);
        FailureIsNotEffective(root, log);
        PartialWriteIsRolledBack(root, log);
        DisableRemovesOnlySuiteValue(root, log);
        ReflectedStateComesFromRegistry(root, log);
    }

    /// <summary>首次使用默认关闭，构造与启动都不得写注册表。</summary>
    private static void DefaultOff(string root, List<string> log)
    {
        var registry = new FakeRunStore();
        SeedOldStartupEntries(registry);
        var controller = new SuiteController(new SuiteSettingsStore(Path.Combine(root, "default")), [],
            new SuiteAutoStart(registry, InstalledExe));
        controller.StartAsync().GetAwaiter().GetResult();
        Assert(!controller.Settings.StartWithWindows, "首次开机启动必须默认关闭");
        Assert(registry.Writes.Count == 0 && registry.Deletes.Count == 0, "首次启动不得写入或删除启动项");
        Assert(registry.Values[OldClipboard] == OldClipboardCommand, "不得触碰原剪贴板启动项");
        log.Add("开机启动默认关闭，首次启动不改动任何启动项");
    }

    private static void EnableWritesOnlySuiteValue(string root, List<string> log)
    {
        var registry = new FakeRunStore();
        SeedOldStartupEntries(registry);
        var store = new SuiteSettingsStore(Path.Combine(root, "enable"));
        var controller = new SuiteController(store, [], new SuiteAutoStart(registry, InstalledExe));
        Assert(controller.SetStartWithWindows(true), "注册开机启动应成功");
        Assert(registry.Writes.Count == 1 && registry.Writes[0] == SuiteAutoStart.ValueName,
            "只允许写入套件自己的启动值名：" + string.Join(",", registry.Writes));
        Assert(registry.Values[SuiteAutoStart.ValueName] == '"' + InstalledExe + '"',
            "启动命令必须指向正式 EXE：" + registry.Values[SuiteAutoStart.ValueName]);
        Assert(controller.Settings.StartWithWindows && store.Load().StartWithWindows, "成功注册后设置应记为开启");
        Assert(registry.Values[OldClipboard] == OldClipboardCommand
            && registry.Values[OldPerf] == OldPerfCommand
            && registry.Values[OldPinto] == OldPintoCommand, "不得改动原三个应用的启动项");
        Assert(controller.AutoStartError is null, "成功时不得保留失败信息");
        log.Add("启用只写入 HKCU Run 的 ToolSuite.Suite 值并指向正式 EXE，原应用启动项不变");
    }

    /// <summary>注册失败必须如实反馈到设置界面，不能把失败记为已生效，也不能留下半写状态。</summary>
    private static void FailureIsNotEffective(string root, List<string> log)
    {
        var registry = new FakeRunStore { FailWrites = true };
        var store = new SuiteSettingsStore(Path.Combine(root, "failure"));
        var controller = new SuiteController(store, [], new SuiteAutoStart(registry, InstalledExe));
        var errors = new List<string>();
        controller.ErrorReported += errors.Add;
        var host = new SettingsHost(controller);
        try
        {
            var window = host.Show();
            var toggle = window.StartWithWindowsToggleForVerification;
            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(!controller.Settings.StartWithWindows, "注册失败不得记为已生效");
            Assert(!store.Load().StartWithWindows, "注册失败不得写入设置文件");
            Assert(!registry.Values.ContainsKey(SuiteAutoStart.ValueName), "注册失败不得留下启动项");
            Assert(toggle.IsChecked == false, "设置界面必须把开关恢复为关闭：" + toggle.IsChecked);
            Assert(window.AutoStartStatusForVerification.Text.Contains("拒绝访问模拟"),
                "设置界面必须显示失败原因：" + window.AutoStartStatusForVerification.Text);
            Assert(errors.Any(x => x.Contains("拒绝访问模拟")), "失败原因必须上报到统一错误入口");
        }
        finally { host.Close(); }
        log.Add("注册失败如实显示在设置界面且不记为生效，开关回到关闭");
    }

    /// <summary>写入已落盘后才报错：必须回滚为操作前状态，不能留下“界面关闭但实际会自启”的残留。</summary>
    private static void PartialWriteIsRolledBack(string root, List<string> log)
    {
        var registry = new FakeRunStore { FailAfterWrite = true };
        var store = new SuiteSettingsStore(Path.Combine(root, "partial"));
        var controller = new SuiteController(store, [], new SuiteAutoStart(registry, InstalledExe));
        Assert(!controller.SetStartWithWindows(true), "写入后报错必须返回失败");
        Assert(!controller.Settings.StartWithWindows && !store.Load().StartWithWindows, "部分写入失败不得记为开启");
        Assert(!registry.Values.ContainsKey(SuiteAutoStart.ValueName), "部分写入失败必须回滚注册表值");
        Assert(controller.AutoStartError!.Contains("写入后报错模拟"), "必须保留真实失败原因");
        log.Add("注册项已写入后报错时回滚，不留下实际生效但界面显示关闭的残留");
    }

    private static void DisableRemovesOnlySuiteValue(string root, List<string> log)
    {
        var registry = new FakeRunStore();
        SeedOldStartupEntries(registry);
        var store = new SuiteSettingsStore(Path.Combine(root, "disable"));
        var controller = new SuiteController(store, [], new SuiteAutoStart(registry, InstalledExe));
        Assert(controller.SetStartWithWindows(true), "注册开机启动应成功");
        Assert(controller.SetStartWithWindows(false), "注销开机启动应成功");
        Assert(!registry.Values.ContainsKey(SuiteAutoStart.ValueName), "注销必须删除套件自己的启动值");
        Assert(registry.Deletes.Count == 1 && registry.Deletes[0] == SuiteAutoStart.ValueName,
            "只允许删除套件自己的启动值名：" + string.Join(",", registry.Deletes));
        Assert(!controller.Settings.StartWithWindows && !store.Load().StartWithWindows, "注销后设置应记为关闭");
        Assert(registry.Values.Count == 3 && registry.Values[OldClipboard] == OldClipboardCommand
            && registry.Values[OldPerf] == OldPerfCommand && registry.Values[OldPinto] == OldPintoCommand,
            "注销不得删除原三个应用的启动项");

        registry.FailDeletes = true;
        Assert(controller.SetStartWithWindows(true), "重新注册应成功");
        Assert(!controller.SetStartWithWindows(false), "注销失败必须返回失败");
        Assert(controller.Settings.StartWithWindows, "注销失败不得把设置记为关闭");
        Assert(controller.AutoStartError!.Contains("拒绝访问模拟"), "注销失败必须保留原因");
        log.Add("注销只删除套件自己的启动值，失败时保留开启状态并报告原因");
    }

    /// <summary>界面与设置以注册表实际状态为准：外部删除后不得继续显示为已生效。</summary>
    private static void ReflectedStateComesFromRegistry(string root, List<string> log)
    {
        var registry = new FakeRunStore();
        var store = new SuiteSettingsStore(Path.Combine(root, "reflect"));
        var stale = store.Load();
        stale.StartWithWindows = true;
        store.Save(stale);
        var controller = new SuiteController(store, [], new SuiteAutoStart(registry, InstalledExe));
        Assert(!controller.Settings.StartWithWindows, "注册表没有套件启动项时不得显示为开启");
        registry.Values[SuiteAutoStart.ValueName] = '"' + InstalledExe + '"';
        var registered = new SuiteController(new SuiteSettingsStore(Path.Combine(root, "reflect-2")), [],
            new SuiteAutoStart(registry, InstalledExe));
        Assert(registered.Settings.StartWithWindows, "注册表存在套件启动项时应显示为开启");
        log.Add("开机启动状态以套件自己的注册表值为准，外部变更不会伪装成已生效");
    }

    /// <summary>登录启动走同一宿主启动入口：只启动已启用工具，停用工具与窗口可见状态沿用套件保存数据。</summary>
    internal static async Task RunStartupSelectionAsync(string root, List<string> log)
    {
        var store = new SuiteSettingsStore(root);
        var settings = new SuiteSettings();
        settings.EnabledTools["clipboard"] = true;
        settings.EnabledTools["perf-monitor"] = false;
        settings.EnabledTools["pinto"] = false;
        store.Save(settings);
        RecordingModule[] modules =
        [
            new("clipboard", "剪贴板"), new("perf-monitor", "性能条"), new("pinto", "便签")
        ];
        var controller = new SuiteController(store, modules, new SuiteAutoStart(new FakeRunStore(), InstalledExe));
        await controller.StartAsync();
        Assert(modules[0].Started, "登录启动必须启动已启用的剪贴板");
        Assert(!modules[1].Started && !modules[2].Started, "登录启动不得重新启用停用的工具");
        Assert(controller.Tools.Where(x => !x.Enabled).All(x => x.State != ToolState.Running),
            "停用工具在登录启动后必须保持停用");
        log.Add("登录启动只启动已启用工具，不重新启用停用工具；窗口可见性由套件保存状态恢复");
        await RunWindowVisibilityAsync(Path.Combine(root, "window-visibility"), log);
    }

    /// <summary>真实便签模块：登录启动只重建保存为可见的便签，隐藏便签不得被强制显示。</summary>
    private static async Task RunWindowVisibilityAsync(string root, List<string> log)
    {
        var hiddenId = Guid.NewGuid();
        var visibleId = Guid.NewGuid();
        var dataDirectory = Path.Combine(root, "modules", "pinto");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "state.json"), $$"""
            {
              "format": "Pinto",
              "schemaVersion": 1,
              "theme": "System",
              "notes": [
                { "id": "{{hiddenId}}", "title": "隐藏便签", "accent": "Blue", "width": 324, "height": 273, "isVisible": false, "dockEdges": "None", "items": [] },
                { "id": "{{visibleId}}", "title": "显示便签", "accent": "Blue", "width": 324, "height": 273, "isVisible": true, "dockEdges": "None", "items": [] }
              ]
            }
            """);
        var store = new SuiteSettingsStore(root);
        var settings = new SuiteSettings();
        settings.EnabledTools["clipboard"] = false;
        settings.EnabledTools["perf-monitor"] = false;
        settings.EnabledTools["pinto"] = true;
        store.Save(settings);

        var controller = new SuiteController(store, [new Pinto.PintoModule()],
            new SuiteAutoStart(new FakeRunStore(), InstalledExe));
        await controller.StartAsync();
        var notes = Application.Current.Windows.OfType<Pinto.Views.NoteWindow>().ToArray();
        Assert(notes.Length == 1 && notes[0].Model.Id == visibleId,
            "登录启动必须只恢复保存为可见的便签，隐藏便签不得被强制显示；实际窗口数 " + notes.Length);
        Assert(notes.All(x => x.Model.Id != hiddenId), "隐藏便签不得创建窗口");
        await controller.PrepareExitAsync();
        await controller.StopAsync();
        Assert(!Application.Current.Windows.OfType<Pinto.Views.NoteWindow>().Any(), "停止便签后窗口必须释放");
        var saved = File.ReadAllText(Path.Combine(dataDirectory, "state.json"));
        Assert(saved.Contains(hiddenId.ToString()) && saved.Contains("\"isVisible\": false"),
            "隐藏便签的可见状态必须保持保存值");
        log.Add("真实便签模块登录启动只显示保存为可见的便签，隐藏便签保持隐藏且状态不变");
    }

    private const string OldClipboard = "ClipboardTool";
    private const string OldPerf = "PerfMonitor";
    private const string OldPinto = "Pinto";
    private const string OldClipboardCommand = @"""C:\Old\Clipboard\ClipboardTool.exe""";
    private const string OldPerfCommand = @"""C:\Old\Perf\PerfMonitor.exe""";
    private const string OldPintoCommand = @"""C:\Old\Pinto\Pinto.exe""";

    private static void SeedOldStartupEntries(FakeRunStore registry)
    {
        registry.Values[OldClipboard] = OldClipboardCommand;
        registry.Values[OldPerf] = OldPerfCommand;
        registry.Values[OldPinto] = OldPintoCommand;
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
