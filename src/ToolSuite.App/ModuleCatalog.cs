using ToolSuite.Core;

namespace ToolSuite.App;

/// <summary>唯一组合根。后续工单在此接入模块适配器并添加 ProjectReference。
/// 模块不得创建 Application、独立托盘、自启身份或独立退出进程。</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IToolModule> CreateModules() => [new ClipboardTool.Presentation.Wpf.ClipboardModule()];
}
