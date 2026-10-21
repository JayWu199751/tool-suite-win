# 工具套件

把剪贴板历史、性能悬浮条和清单便签整合为一个安装包、一个托盘和一个常驻管理员权限 WPF 宿主。
三个工具在同一进程内运行、各自可独立启停，使用套件独立的数据目录，与原三个应用互不影响。

- 版本：1.0.0（Windows 11 x64，自包含 .NET 10 桌面运行时）
- 安装包：`artifacts/installer/ToolSuite-Setup-1.0.0.exe`
- 操作手册：[交付说明](docs/handoff.md)
- 最终验证：[工单 09 验证记录](docs/validation/ticket-09.md)

## 快速开始

安装（当前用户，无需管理员权限）：

```powershell
& artifacts/installer/ToolSuite-Setup-1.0.0.exe
# 静默：& artifacts/installer/ToolSuite-Setup-1.0.0.exe /silent /dir="D:\Apps\ToolSuite"
```

从开始菜单启动“工具套件”并在 UAC 中允许（正式 EXE 要求管理员权限）。首次启动同时启用三个工具：
性能条显示、便签建立一张空白便签、剪贴板按 `Ctrl+Shift+V` 呼出。托盘左键与双击无动作，
右键按工具分组管理，`统一设置` 里可改主题、启停工具与开启开机启动（默认关闭）。
卸载只删除程序与套件自启值，保留 `%LOCALAPPDATA%\ToolSuite` 用户数据。

操作步骤、升级/卸载、UAC 行为、便签导入导出与人工验收清单见[交付说明](docs/handoff.md)。

## 构建与检查

需要 Windows 11 x64 与 .NET 10 SDK。命令在仓库根执行；`dotnet` 用绝对路径或把 `DOTNET_ROOT`
指向 SDK 安装根。

```powershell
$dotnet = "C:/Users/10854/AppData/Local/Microsoft/dotnet/dotnet.exe"
& $dotnet build ToolSuite.slnx -c Release          # 期望 0 警告 0 错误
./scripts/verify-host.ps1                          # 隔离数据下的宿主控制入口与真实 WPF 检查
./scripts/build-installer.ps1                      # artifacts/publish、artifacts/installer
./scripts/verify-installer.ps1                     # 自包含发布与安装器隔离检查
./scripts/verify-installer.ps1 -Smoke              # 追加真实安装包的临时目录静默安装/卸载冒烟
```

检查逐工程运行（并行运行 WPF 检查会互相干扰）：

```powershell
& $dotnet test modules/Clipboard/ClipboardTool.Tests -c Release --no-build
& $dotnet test modules/PerfMonitor/tests/PerfMonitor.Core.Tests -c Release --no-build
& $dotnet test modules/PerfMonitor/tests/PerfMonitor.Windows.Tests -c Release --no-build
& $dotnet test modules/PerfMonitor/tests/PerfMonitor.App.Tests -c Release --no-build
& $dotnet modules/Pinto/tests/Pinto.Checks/bin/Release/net10.0/Pinto.Checks.dll artifacts/pinto-checks
& $dotnet modules/Pinto/tests/Pinto.ModuleChecks/bin/Release/net10.0-windows/Pinto.ModuleChecks.dll artifacts/pinto-module
& $dotnet modules/Pinto/tests/Pinto.DesktopChecks/bin/Release/net10.0-windows/Pinto.DesktopChecks.dll artifacts/pinto-desktop
& $dotnet tests/ToolSuite.HostChecks/bin/Release/net10.0/ToolSuite.HostChecks.dll artifacts/host
& $dotnet tests/ToolSuite.ClipboardChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardChecks.dll artifacts/clip
& $dotnet tests/ToolSuite.ClipboardUiChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardUiChecks.dll artifacts/clip-ui
& $dotnet tests/ToolSuite.SuiteChecks/bin/Release/net10.0-windows/ToolSuite.SuiteChecks.dll artifacts/suite
& $dotnet tests/ToolSuite.LifecycleChecks/bin/Release/net10.0-windows/ToolSuite.LifecycleChecks.dll artifacts/lifecycle
& $dotnet tests/ToolSuite.InstallerChecks/bin/Release/net10.0-windows/ToolSuite.InstallerChecks.dll artifacts/installer
```

所有检查使用 `artifacts/` 下的隔离数据根，不读写日常套件数据、真实开机启动项或原三个应用。
`WindowSummonOrderTests`（剪贴板顶层 z 序）在无交互桌面会话下不稳定，见工单 09 验证记录。

## 项目结构

```
ToolSuite.slnx                 解决方案（src / modules / tests / tools）
src/ToolSuite.Core             宿主控制器、模块契约、统一设置、自启抽象、套件身份
src/ToolSuite.App              WPF 宿主：托盘、设置窗口、主题、单实例、程序入口
modules/Clipboard              剪贴板领域/应用/Windows 适配/WPF 界面与测试
modules/PerfMonitor            性能条 Core/Windows/App 与三套测试
modules/Pinto                  便签 Core/App 与数据、模块、桌面检查
tests/ToolSuite.HostChecks     宿主控制入口检查（无 UI）
tests/ToolSuite.ClipboardChecks   剪贴板领域与存储检查
tests/ToolSuite.ClipboardUiChecks 剪贴板真实 WPF 面板检查
tests/ToolSuite.SuiteChecks    统一控制、主题、启停与退出整合检查
tests/ToolSuite.LifecycleChecks 隐藏界面回收与重复重建检查
tests/ToolSuite.InstallerChecks 开机启动与安装器隔离检查
tools/ToolSuite.Installer      单文件安装器与其核心逻辑
scripts                        build-installer.ps1 / verify-host.ps1 / verify-installer.ps1
docs                           交付说明、模块契约、ADR、逐工单验证记录
artifacts/                     构建、发布、安装包与隔离检查产物（不入库）
```

## 已确认边界

- 托盘左键及双击无操作，剪贴板只通过全局快捷键呼出；右键按三个工具分组并提供统一设置与退出。
- 三个工具可独立启停并记住状态，统一主题（跟随系统/浅色/深色），统一开机启动首次默认关闭。
- 新套件使用 `%LOCALAPPDATA%\ToolSuite` 独立数据，从空白开始；原应用、原数据和旧启动项保持独立，
  不执行自动迁移或旧版接管。
- 隐藏不等于停用：隐藏保留内容并按需释放窗口，停用停止后台工作并在重新启用后恢复可见状态。
- 保留原有手动便签备份导入/导出。隐藏与停用均保留内容，必要保存失败时不丢弃编辑。
- 保留功能测试，内存表现由日常使用判断；不安排专项内存对比，也不承诺具体节省数值。
- 所有代码、文档、测试、脚本及构建产物位于本仓库。原三个项目保持不动。

## 文档

- [交付说明](docs/handoff.md)：安装/升级/卸载、权限、托盘、快捷键、启停、便签备份、主题、自启、内存观察。
- [模块接入约定](docs/module-integration.md)
- [正式规格](spec.md)：产品行为、实现决定、功能测试和范围边界。
- [工单索引与依赖图](tickets/README.md)
- [术语表](GLOSSARY.md)
- [ADR-0002：一个管理员权限 WPF 宿主](docs/adr/0002-single-elevated-wpf-host.md)
- [ADR-0003：独立数据并从空白开始](docs/adr/0003-independent-fresh-data.md)
- [原项目来源记录](docs/source-provenance.md)
- 逐工单验证：[01](docs/validation/ticket-01.md)、[03](docs/validation/ticket-03.md)、
  [06](docs/validation/ticket-06.md)、[07](docs/validation/ticket-07.md)、
  [08](docs/validation/ticket-08.md)、[09](docs/validation/ticket-09.md)
- 背景：[讨论记录](discussion.md)、[方案摘要](plan.md)、[工单拆分记录](ticket-breakdown.md)。
