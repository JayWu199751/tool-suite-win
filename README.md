# Tool Suite Win · 工具套件

把**剪贴板历史、性能悬浮条、清单便签、循环提醒**放在一起的 Windows 桌面工具。
一个安装包、一个托盘、一个常驻进程，四个工具可以独立启停。

**Windows 11 x64 · C# / WPF · .NET 10**

[界面预览](#界面预览) · [快速开始](#快速开始) · [构建安装包](#构建安装包) · [项目文档](docs/README.md)

## 功能

| 工具 | 能做什么 |
| --- | --- |
| 剪贴板 | 自动记录文字、图片与截图；搜索历史、置顶、编辑备注；选中后复制或粘贴回原窗口。默认快捷键 **Ctrl + Shift + V**。 |
| 性能条 | 显示 CPU、内存、GPU、显存、网络速度与时间；调整指标、刷新率、字号、不透明度；支持全屏自动隐藏、透明显示及任务栏水平居中。硬件指标以设备和驱动支持为准。 |
| 便签 | 多张桌面清单，支持完成状态、排序、独立强调色、屏幕及便签间贴边；可导入、导出备份。 |
| 循环提醒 | 创建多项提醒，按秒、分钟或小时设置间隔；到期聚合为置顶小窗，确认后重新计时。支持独立启停、编辑与删除。 |
| 统一设置 | 顶部管理主题与开机启动，下方分别展开四个工具设置；修改即时保存。支持浅色、深色和跟随系统。 |

四个工具共用宿主，各自保留配置与数据。隐藏窗口保留内容，停用工具则停止对应后台工作。

## 界面预览

以下图片由当前 WPF 界面使用示例数据渲染；剪贴板文字、便签清单和性能数值均为演示内容。

### 统一设置

通用设置始终可见，四个工具分类独立展开、收起并记住状态。窗口固定为 380 × 720 DIP，小屏按工作区调整高度，长内容在内部滚动。以下统一设置旧预览尚未展示新增的循环提醒分类。

| 浅色 | 深色 |
| :---: | :---: |
| <img src="docs/images/settings-light.png" alt="浅色统一设置：主题、开机启动及三个工具分类" width="300"> | <img src="docs/images/settings-dark.png" alt="深色统一设置：主题、开机启动及三个工具分类" width="300"> |

### 剪贴板与清单便签

按快捷键查找最近复制的内容，把待办事项留在桌面。

| 剪贴板历史 | 清单便签 |
| :---: | :---: |
| <img src="docs/images/clipboard-dark.png" alt="剪贴板历史：搜索框、置顶条目、来源和备注" width="330"> | <img src="docs/images/notes-dark.png" alt="桌面便签：示例清单、完成状态和新增事项输入框" width="324"> |

### 性能悬浮条

<img src="docs/images/performance-dark.png" alt="性能条：CPU、内存、GPU、显存、网络上下行速率与时钟" width="760">

### 循环提醒

托盘右键 → **循环提醒 → 新增提醒…**，直接打开独立的新增窗口，填写名称和间隔后点击“创建提醒”。默认间隔 30 分钟，可改为秒、分钟或小时。取消或关闭窗口不创建提醒；重复点击新增保留已有草稿。

**管理循环提醒…**定位到统一设置的提醒列表，可启停、编辑或删除提醒；“编辑”也使用独立窗口，设置页不包含输入表单。提醒工具停用时仍可新增或编辑，保存后需启用工具才会计时。

到期时弹出一个置顶、无声且不抢焦点的窗口。逐项点击“知道了”开始下一轮；关闭整个弹窗表示全部确认。睡眠、锁屏时间计入间隔，恢复并解锁后最多补一次提醒；退出或停用工具后不提醒，再次启用按完整间隔开始。

| 管理提醒（浅色） | 到期弹窗（深色） |
| :---: | :---: |
| <img src="docs/images/reminders-settings-light.png" alt="循环提醒管理：名称、间隔、剩余时间和独立开关" width="270"> | <img src="docs/images/reminders-popup-dark.png" alt="多项提醒聚合弹窗，逐项点击知道了" width="330"> |

<img src="docs/images/reminders-editor-dark.png" alt="独立的新增提醒窗口：名称、间隔与单位、取消和创建提醒" width="360">

## 快速开始

当前仓库提供源码，安装包可按下文自行构建。开发环境需要 **Windows 11 x64、.NET 10 SDK 和 PowerShell 7**；`dotnet` 需在 PATH 中可用。

```powershell
git clone https://github.com/JayWu199751/tool-suite-win.git
cd tool-suite-win
dotnet build ToolSuite.slnx -c Release
& .\src\ToolSuite.App\bin\Release\net10.0-windows\ToolSuite.exe
```

正式应用启动时会请求 **管理员权限**，用于保留向管理员窗口直接粘贴的能力；允许 UAC 后，四个工具运行在同一个提权进程中。拒绝授权则本次整个套件不启动。

首次启动会启用四个工具：性能条显示在桌面，便签建立一张空白清单，剪贴板等待快捷键呼出；循环提醒初始为空，添加后才开始计时。

| 操作 | 入口 |
| --- | --- |
| 呼出或收起剪贴板 | `Ctrl + Shift + V`，可在统一设置中修改 |
| 管理工具、新建便签、显隐窗口 | 右键点击系统托盘中的工具套件图标 |
| 打开设置 | 托盘右键 → **统一设置**；工具自己的设置入口也会定位到同一页面 |
| 开机启动 | 统一设置顶部的开关，首次默认关闭；开启后通过当前用户的最高权限登录任务启动 |
| 退出 | 托盘右键 → **退出工具套件**，退出前保存内容 |

托盘左键单击、双击均无动作。重复启动会打开现有实例的设置页。

## 构建安装包

在仓库根目录执行：

```powershell
.\scripts\build-installer.ps1
```

脚本生成自包含的 Windows x64 安装包，目标机器无需预装 .NET 运行时：

```text
artifacts/installer/ToolSuite-Setup-1.0.0.exe
```

双击安装包后，从开始菜单启动“工具套件”。安装器本身无需管理员权限，默认安装到 `%LOCALAPPDATA%\Programs\ToolSuite`。

如 SDK 不在 PATH 中，可通过 `-Dotnet 'C:\你的SDK目录\dotnet.exe'` 指定路径。升级前先从托盘退出应用。
静默安装、升级回滚、卸载和权限说明见[交付说明](docs/handoff.md)。

## 数据与备份

- 套件数据保存在 `%LOCALAPPDATA%\ToolSuite`，与原三个独立应用的数据分开，不自动迁移旧数据。
- 隐藏或停用工具均保留内容；关闭便签表示隐藏，可从托盘再次打开。
- 便签通过托盘或统一设置提供手动备份导入、导出入口。
- 卸载保留用户数据，重新安装后可继续使用。

## 开发与验证

```powershell
# 构建整个解决方案
dotnet build ToolSuite.slnx -c Release

# 宿主控制入口、真实 WPF 窗口与单实例检查
.\scripts\verify-host.ps1

# 统一设置页面检查；使用新的隔离目录
dotnet run --project tests/ToolSuite.SuiteChecks -c Release -- artifacts/readme-check --unified-settings

# 循环提醒计时、持久化及真实 WPF 界面检查；每次使用新的隔离目录
dotnet run --project tests/ToolSuite.ReminderChecks -c Release -- artifacts/reminder-check

# 自包含发布与安装器检查
.\scripts\verify-installer.ps1
```

运行构建或安装器检查前，请先退出正在使用同一输出目录的应用，避免程序文件被占用。
检查使用 `artifacts/` 下的隔离数据；WPF 桌面检查应逐项运行，避免相互干扰。
详细结果与验证边界见[统一设置验证记录](docs/validation/unified-settings.md)及[整套验收记录](docs/validation/ticket-09.md)。

## 项目结构

```text
src/
  ToolSuite.App/          WPF 宿主、托盘、统一设置、主题与单实例
  ToolSuite.Core/         模块契约、控制器、套件配置与启动抽象
modules/
  Clipboard/             剪贴板历史与面板
  PerfMonitor/           性能采样与悬浮条
  Pinto/                 清单便签与备份
  Reminders/             循环提醒、调度与聚合弹窗
tests/                  宿主、模块整合、生命周期及安装器检查
tools/                  安装器
scripts/                构建与验证脚本
docs/                   规格、设计、ADR、工单与验证记录
```

构建、安装包、缓存和临时检查产物不提交到 Git；README 使用的界面图片保存在 `docs/images/`。

## 文档与反馈

- [文档索引](docs/README.md)：全部维护文档入口。
- [使用与交付说明](docs/handoff.md)：安装、升级、卸载、托盘操作、权限与备份。
- [统一设置设计](docs/unified-settings.md)：入口、分组与保存行为。
- [正式规格](docs/spec.md)与[术语表](GLOSSARY.md)：产品行为和共享概念。
- [模块接入约定](docs/module-integration.md)、[设计系统](docs/design-system.md)、[来源记录](docs/source-provenance.md)。

遇到问题或有功能建议，欢迎提交 [Issue](https://github.com/JayWu199751/tool-suite-win/issues)，附上 Windows 版本、复现步骤和相关截图。
