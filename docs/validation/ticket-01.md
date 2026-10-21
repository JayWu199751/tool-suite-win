# 工单 01 验证

日期：2026-10-21。实现分支 `ticket/01-host`，基于集成提交 `56fa6712fcc9f4c54edc61c91460a9edd1eb1551`。

按已确认入口（真实宿主、托盘命令、配置恢复和隔离存储）进行 red → green；未读取原工具用户数据，未改原仓库、真实自启或安装身份。

## 红绿证据

1. 先建立 HostChecks 的首次偏好、独立模块目录、主题及停用偏好恢复检查。首次运行因 Core 项目/类型缺失失败；实现独立设置存储后通过。
2. 增加公开 SuiteController 入口的启动、未接入状态、保存失败拒绝停用及退出、保存成功后重新启用检查。首次运行因模块契约类型缺失失败；实现真实控制器后通过。
3. 在原为停用的工具启用时，用真实文件系统令 suite.json.tmp 目标成为目录，阻止启用偏好落盘。首次运行观测到“模块仍 Running”而失败；加入启动资源回滚后通过，落盘偏好继续停用。
4. 首次 WPF 构建因 Forms/WPF 同名类型冲突失败；限定类型后 Release 构建成功，0 警告、0 错误。

## 已运行

- `dotnet run --project tests/ToolSuite.HostChecks -- artifacts/green-03`：以上三个检查组通过。
- `dotnet build src/ToolSuite.App -c Release`：成功。
- `dotnet src/ToolSuite.App/bin/Release/net10.0-windows/ToolSuite.dll --data-root artifacts/wpf-01 --verify-host`：真实 WPF 运行成功退出，产生 verification-passed.txt。
- WPF 检查实际验证无首页、通过真实托盘菜单控件事件打开设置、关闭设置后没有存活窗口、第二个真实进程通知原实例后退出、原实例显示设置反馈、托盘退出命令完成保存和退出。
- 统一 `scripts/verify-host.ps1` 可重跑上述检查，每次使用 artifacts 内新建隔离根；不访问个人数据或真实自启。

模块启用偏好首次都为 true，但未注册模块真实状态为 Unavailable。所有产物及 SDK CLI/包缓存位于 tool-suite 范围，项目文件没有开发机绝对路径依赖。

## 未执行的人工场景

- 正式 ToolSuite.exe 触发 UAC、确认和拒绝授权：manifest 已设置 requireAdministrator；自动检查经 dotnet DLL 绕开 EXE manifest，不能据此宣称 UAC 已通过。请在干净隔离根手动启动 EXE，拒绝时应没有套件进程/托盘；接受时只有一个套件托盘。
- 系统托盘真实鼠标左键、双击和右键：代码仅绑定右键 ContextMenuStrip，未绑定左键/双击；自动检查调用真实菜单控件事件，未用系统鼠标实际点击托盘图标。人工确认左键和双击无窗口动作、右键有三个分组及设置退出。
- 系统切换主题、高对比度与真实多 DPI 辅助功能：已接入共用系统监听和高对比度宿主色刷，真实操作留待完整整合回归。

工单 01 的实现已完成；上述 OS 交互不冒记通过。正式交付验收会再次覆盖它们。
