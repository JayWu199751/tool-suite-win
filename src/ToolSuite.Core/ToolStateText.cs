namespace ToolSuite.Core;

/// <summary>工具状态文案的唯一映射：短标签用于托盘分组标题，详情用于统一设置。
/// 两处各自 switch 会出现同一状态两套措辞，因此集中在这里。</summary>
public static class ToolStateText
{
    /// <summary>托盘分组标题里的状态短标签，保持简短可读。</summary>
    public static string Short(ToolState state) => state switch
    {
        ToolState.Running => "运行中",
        ToolState.Disabled => "已停用",
        ToolState.Failed => "启动失败",
        _ => "不可用"
    };

    /// <summary>统一设置里的状态详情；“不可用”必须保留契约含义（尚未接入、本版本不可用、偏好保留）。</summary>
    public static string Detail(ToolStatus tool) => tool.State switch
    {
        ToolState.Unavailable => "尚未接入，此版本不可用；启用偏好已保留。",
        ToolState.Running => tool.Error is null ? "运行中" : $"运行中；操作失败：{tool.Error}",
        ToolState.Disabled => "已停用",
        _ => $"启动或停止失败：{tool.Error}（可重新启用重试）"
    };
}
