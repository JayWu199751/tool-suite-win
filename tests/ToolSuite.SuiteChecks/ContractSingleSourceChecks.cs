using System.IO;
using ToolSuite.Core;

namespace ToolSuite.SuiteChecks;

/// <summary>单一真源检查：模块标识顺序、设置补全与工具状态文案都只能有一份。
/// 期望值取自 docs/module-integration.md 的公开契约，不读私有字段。</summary>
internal static class ContractSingleSourceChecks
{
    internal static void Run(string root, List<string> log)
    {
        CatalogContract(root);
        StateTextContract();
        log.Add("模块标识与状态文案来自 ToolSuite.Core 的单一真源，设置补全不遗漏也不新造工具");
    }

    private static void CatalogContract(string root)
    {
        var ids = ToolIds.Catalog.Select(x => x.Id).ToArray();
        Assert(ids.SequenceEqual(new[] { "clipboard", "perf-monitor", "pinto", "reminders" }),
            "模块标识顺序必须与公开契约一致：" + string.Join(",", ids));
        Assert(ToolIds.Catalog.Select(x => x.Name).SequenceEqual(new[] { "剪贴板", "性能条", "便签", "循环提醒" }),
            "模块显示名必须与公开契约一致");

        var defaults = new SuiteSettings().EnabledTools;
        Assert(defaults.Count == ids.Length && ids.All(id => defaults.TryGetValue(id, out var on) && on),
            "默认设置必须包含全部登记工具且全部启用；新提醒工具初始没有提醒");

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "suite.json"),
            """{"Theme":1,"StartWithWindows":false,"EnabledTools":{"pinto":false}}""");
        var loaded = new SuiteSettingsStore(root).Load();
        Assert(loaded.EnabledTools.Count == ids.Length, "读取旧设置只能补齐契约里的工具，不得新造标识");
        Assert(ids.Where(id => id != "pinto").All(id => loaded.EnabledTools[id]), "缺失的工具按启用补齐");
        Assert(!loaded.EnabledTools["pinto"], "已有选择必须保留");
    }

    private static void StateTextContract()
    {
        foreach (var state in Enum.GetValues<ToolState>())
        {
            var shortText = ToolStateText.Short(state);
            Assert(!string.IsNullOrWhiteSpace(shortText) && shortText.Length <= 6, "托盘组标题必须简短可读：" + shortText);
        }

        Assert(ToolStateText.Short(ToolState.Running) == "运行中", "运行中的短标签");
        Assert(ToolStateText.Short(ToolState.Failed) == "启动失败", "启动失败的短标签");
        Assert(ToolStateText.Detail(Status(ToolState.Unavailable, null)).Contains("尚未接入，此版本不可用"),
            "不可用详情必须保留契约文本");
        Assert(ToolStateText.Detail(Status(ToolState.Running, "写入失败")).Contains("写入失败"), "运行中的错误要如实显示");
        Assert(ToolStateText.Detail(Status(ToolState.Failed, "端口占用")).Contains("端口占用"), "启动失败要显示原因");
        Assert(ToolStateText.Detail(Status(ToolState.Failed, "端口占用")).Contains("重新启用"), "启动失败要给出可操作提示");
    }

    private static ToolStatus Status(ToolState state, string? error) =>
        new("clipboard", "剪贴板", new SuiteSettings().EnabledTools["clipboard"], state, error, []);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
