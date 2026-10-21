# 模块接入约定

工单 01 建立的唯一组合根是 `src/ToolSuite.App/ModuleCatalog.cs`。后续复制原代码到 `modules/Clipboard`、`modules/PerfMonitor`、`modules/Pinto`，在宿主项目添加相对 ProjectReference，并在组合根返回轻量适配器；不得执行旧 App 入口或运行旧 EXE。

公开契约在 `src/ToolSuite.Core/IToolModule.cs`，真实控制入口在 `SuiteController.cs`：

- ID 使用 `clipboard`、`perf-monitor`、`pinto`。
- `StartAsync(ModuleContext, CancellationToken)` 在 UI 线程启动/重建。上下文指定套件模块专用 `DataDirectory`、当前有效 `Theme`（Light 或 Dark）和错误报告入口。每次启用可重新建立窗口；构造函数不分配后台资源。
- `PrepareToStopAsync` 提交编辑、捕获布局、保存；失败抛异常，保留窗口与可恢复内容。宿主在停用及退出前调用。
- `StopAsync` 取消并等待任务、释放窗口/钩子/事件；可重复调用并能清理部分启动。这里不能再执行有失败可能的首次内容保存，也不能全局 Shutdown。
- `ApplyTheme` 使用模块局部资源；宿主统一监听系统主题，不替换其他模块的 Application 字典。
- `Commands` 是动态属性，每次托盘打开读取最新列表，便签可以据此返回当前隐藏标题。`ToolCommand.RequiresRunning=false` 用于停用后仍可打开的离线配置入口；命令不能隐式启用模块。
- 托盘不提供剪贴板面板呼出命令，也不绑定左键及双击；剪贴板适配器自己注册启用期快捷键。

宿主控制器串行执行模块生命周期与菜单命令，调用位于唯一 UI 同步上下文。模块不得创建自己的 Application、常驻进程、托盘、自启或全局退出入口。未注册模块显示“尚未接入，此版本不可用”，仍保留启用偏好，不分配空白假工具窗口。

默认套件数据根为 `%LOCALAPPDATA%/ToolSuite`，统一设置 `suite.json`，模块为 `modules/<id>`。开发检查显式传入 `--data-root`；这里的参数是完整隔离根，不是原应用目录。存储实现必须注入此目录，禁止回退旧默认路径。正式 EXE 的 manifest 要求管理员；验证脚本通过 dotnet 运行 DLL，不声称验证 UAC。

统一设置当前只提供主题与工具启停。开机启动在工单 08 增加真实适配，工单 01 显示关闭且不写注册表/任务计划；统一设置关闭后移除窗口和控制器事件订阅。
