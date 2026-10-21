# 工单 06 验证：统一控制、主题与安全退出

日期：2026-10-21。实现分支 `ticket/06-controls`，基于集成提交 `ecfbe76`。
三个真实工具（剪贴板/性能条/便签）接入宿主；全部检查使用工作树 `artifacts/` 下的隔离数据根，
不读取原应用或套件用户数据，未改动模块内部窗口回收逻辑（工单 07 范围）。

## 红绿证据

1. **红（真实缺陷）**：新增 `tests/ToolSuite.SuiteChecks` 的控制器语义检查，先运行通过公开入口
   执行普通命令与启停，观测到“同一有效外观不得重复分配主题”失败——`SuiteController.ApplyEffectiveTheme`
   每次调用都向所有模块重建主题，`SuiteTheme` 又订阅了通用 `Changed`，任何菜单命令都会整份重解析主题。
   输出：
   `System.Exception: 同一有效外观不得重复分配主题`
2. **绿**：`ApplyEffectiveTheme` 增加有效外观去重，`SetTheme` 单独抛出 `AppearanceChanged`；
   `SuiteTheme` 只在明暗/高对比度键变化时刷新宿主资源并下发主题，系统偏好变化仍强制重算。
   同组检查全部通过。
3. **红（实现缺失）**：主题故障隔离检查首轮抛出未处理异常（`System.InvalidOperationException: 模拟主题刷新故障`
   直接冒泡到调用方）；补上按模块 try/catch、错误记录与上报后通过。
4. **红（真实缺陷）**：失败模块重试检查首轮失败：
   `System.Exception: 重试启动前必须清理残留释放（实际 stop 次数 1）`。
   在启用失败模块前先尽力 `StopAsync` 清理部分启动资源后通过。
5. **敏感度验证（临时移除实现后重跑，确认检查确实会红，随后全部恢复）**：
   - 移除 `SuiteShutdown` 对 `PrepareExitAsync` 结果的判断 → `System.Exception: 保存失败不得静默退出套件`。
   - 移除设置窗口的“重试启动”按钮 → `System.Exception: 设置应对启动失败的工具给出重试动作`。
   - 令 `TrayHost.ApplyAppearance` 直接返回 → `System.Exception: 高对比度托盘必须交还系统配色`。
   - 移除 `ApplyEffectiveTheme` 的模块级隔离 → 主题故障异常再次冒泡（同第 3 条）。
   其余检查（分组命令、全部停用后恢复、可见状态恢复、保存失败保留内容）作为首轮回归护栏保留。
6. **宿主基础检查仍为隔离模式**：`--verify-host` 不注册真实工具，真实 WPF 下运行成功；
   整套模块功能由 `ToolSuite.SuiteChecks` 单独验证，不用空模块通过代替。

## 已运行

- `dotnet build ToolSuite.slnx -c Release` → 0 警告、0 错误。
- `dotnet tests/ToolSuite.SuiteChecks/bin/Release/net10.0-windows/ToolSuite.SuiteChecks.dll artifacts/final-06`
  → 14 条整合检查全部 PASS（真实 WPF/STA，真实三模块，隔离数据根）：
  1. 普通命令/启停不刷新主题；有效外观变化仅下发一次
  2. 单模块 `ApplyTheme` 异常被隔离并报告，其他模块继续接收主题
  3. 失败模块重试前先清理残留，不伪装停用
  4. 单一托盘按剪贴板/性能条/便签分组展示状态与动作，剪贴板无呼出面板动作
  5. 停用便签后剪贴板与性能条继续运行
  6. 统一设置按需创建、关闭释放；主题与启停操作不隐式启用停用工具
  7. 三个工具全部停用后唯一托盘与设置入口仍可重新启用任意工具
  8. 一次有效外观变化只刷新一次；暗色/高对比度托盘配色可读；便签强调色与宿主资源不受主题串扰
  9. 隐藏便签与隐藏性能条在停用/重新启用后保持；已显示便签保持显示
  10. 启停偏好与窗口可见状态分别持久化，重启保持
  11. 停用与退出先保存预检；保存失败不完成停用、不退出且可继续使用
  12. 修复存储后停用/启用可重试，便签内容未丢失
  13. 单模块启动失败显示失败状态与可操作提示，其他模块继续运行
  14. 清理故障后可从托盘与统一设置重试启动失败模块
- `dotnet tests/ToolSuite.HostChecks/bin/Release/net10.0/ToolSuite.HostChecks.dll artifacts/host-final-06`
  → 4 组宿主控制入口检查通过（含工单 01/03 的保存失败、离线上下文、启用回滚）。
- `dotnet src/ToolSuite.App/bin/Release/net10.0-windows/ToolSuite.dll --data-root artifacts/vh-final-06 --verify-host`
  → 退出码 0，`verification-passed.txt`：无首页、托盘打开设置、关闭释放、重复实例通知原实例、安全退出。
- 回归：`PerfMonitor.Core.Tests` 161、`PerfMonitor.Windows.Tests` 50、`PerfMonitor.App.Tests` 22 全部通过；
  `Pinto.ModuleChecks`、`ToolSuite.ClipboardChecks` 全部 PASS。

## 覆盖方式（实现要点）

- 托盘：右键按三个工具分组，组标题直接显示“运行中/已停用/启动失败/不可用”，组内提供启停与模块管理动作，
  另有统一设置与退出；不订阅左键/双击；剪贴板分组只有快捷键与清空历史，没有呼出面板动作。
- 设置：`SettingsHost` 按需创建设置窗口，关闭即移除订阅；失败工具显示错误与“重试启动”按钮。
- 主题：唯一偏好存 `suite.json`，默认跟随系统；`SuiteTheme` 只在有效外观键变化时刷新，模块局部资源互不覆盖，
  托盘同步使用暗色/系统高对比度配色。
- 退出：托盘退出与系统会话结束都先 `PrepareExitAsync`（提交编辑+保存预检），失败保留运行状态并提示；
  会话结束使用嵌套消息帧在 UI 线程等待异步预检，避免同步阻塞死锁，失败时 `e.Cancel = true`。
- 模块启动失败：状态为 Failed、错误上报到托盘气泡与设置行，其他模块继续运行；重试前先清理残留。

## 未执行的人工场景

- 真实鼠标点击系统托盘图标：未用物理鼠标确认左键/双击无动作、右键菜单外观；自动检查只调用真实菜单项事件。
- 系统级高对比度主题、系统文字大小与多显示器/DPI：代码保留 `SystemColors` 与系统字体路径，托盘高对比度配色已用
  内部入口验证；未实际切换系统辅助功能设置做视觉确认。
- 真实注销/关机：会话结束路径已按取消-预检-决定实现，并通过 `SuiteShutdown.PrepareAndStopAsync` 的失败/成功分支验证；
  未真实注销 Windows 会话（会中断检查机）。
- 真实 UAC 启动正式 EXE：沿用工单 01 限制，检查经 dotnet 运行 DLL。
- 便签/性能条原右键菜单中“退出”的视觉存在性：已确认性能条宿主菜单过滤 `Exit`、便签无退出入口，
  且模块命令与操作不会关闭宿主 Application；未逐一人工点击旧菜单确认。

## 风险

- 会话结束路径依赖 UI 线程上的嵌套 `DispatcherFrame`；若模块保存长时间阻塞（例如网络盘），系统关机会被延迟。
- `ApplyEffectiveTheme` 去重后，模块若在运行中自行改变局部主题，需要自身负责恢复，宿主不会重复下发同一主题。
- 托盘暗色配色为自定义 `ProfessionalColorTable`，在非默认 DPI 缩放下仍需人工确认文字与分隔线可读。
