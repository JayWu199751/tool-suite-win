namespace ToolSuite.Core;

/// <summary>模块标识与显示名的唯一真源。控制器的工具顺序、默认启停偏好与设置补全都引用这里，
/// 不在各处重复字面量；组合根仍只负责返回适配器实例。</summary>
public static class ToolIds
{
    public const string Clipboard = "clipboard";
    public const string PerfMonitor = "perf-monitor";
    public const string Pinto = "pinto";
    public const string Reminders = "reminders";

    /// <summary>对外展示顺序即控制器与托盘的顺序，不得随意调整。</summary>
    public static IReadOnlyList<(string Id, string Name)> Catalog { get; } =
        [(Clipboard, "剪贴板"), (PerfMonitor, "性能条"), (Pinto, "便签"), (Reminders, "循环提醒")];

    public static IReadOnlyList<string> All { get; } = Catalog.Select(x => x.Id).ToArray();
}
