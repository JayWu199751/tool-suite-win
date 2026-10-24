# 循环提醒验证记录

日期：2026-10-24。使用 PowerShell 7、用户目录的 .NET 10 SDK，所有检查使用 `artifacts/` 下的隔离数据，没有读取日常提醒数据。

## 自动检查

| 检查 | 结果 |
| --- | --- |
| `dotnet build ToolSuite.slnx -c Release --no-restore --nologo -v quiet` | 通过，0 警告、0 错误 |
| `ToolSuite.ReminderChecks` | 45 项通过，包括可控计时源、真实文件保存失败、独立新增／编辑窗口与聚合弹窗 |
| `ToolSuite.SuiteChecks --unified-settings` | 通过，包括真实托盘新增入口、独立窗口、不创建设置窗口、第四分类、离线创建、独立启停以及原有设置回归 |
| `ToolSuite.HostChecks` | 通过，独立数据、保存恢复、真实启停、保存失败回滚和停用时配置 |
| `ToolSuite.LifecycleChecks` | 通过，原有三个模块的隐藏回收、重建、失败保留、退出释放与重复启停 |
| 完整 `ToolSuite.SuiteChecks` | 未通过：宿主托盘图标尺寸断言得到 24 像素，系统 `SmallIconSize` 为 28 像素；在提醒无关的 `HostIntegrationChecks.cs` 图标检查处中断，不能宣称完整宿主回归通过 |

完整检查的阻断位置为 `HostIntegrationChecks.TrayAndSettingsAsync`，发生在该场景开始启动模块之前。本次没有修改 `TrayHost.LoadTrayIcon` 或应用图标资源，也没有放宽图标断言。其他已完成的检查结果如上分别列明。

## 可复现命令

当前机器 PATH 中的 `dotnet` 只有运行时，使用已安装的用户目录 SDK：

```powershell
$sdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
& $sdk build ToolSuite.slnx -c Release --nologo
& $sdk run --project tests/ToolSuite.ReminderChecks -c Release --no-build -- artifacts/reminder-checks/new-run
& $sdk run --project tests/ToolSuite.SuiteChecks -c Release --no-build -- artifacts/reminder-suite-checks/new-run --unified-settings
& $sdk run --project tests/ToolSuite.HostChecks -c Release --no-build -- artifacts/reminder-host-checks/new-run
& $sdk run --project tests/ToolSuite.LifecycleChecks -c Release --no-build -- artifacts/reminder-lifecycle-checks/new-run
```

每次选用新的检查目录。真实 WPF 检查逐项运行，避免窗口相互干扰。

2026-10-24 的独立窗口改动使用独立输出目录，避免替换当前正在运行的旧版程序集：

```powershell
& $sdk build ToolSuite.slnx -c Release --artifacts-path artifacts/reminder-editor-build --nologo
& $sdk artifacts/reminder-editor-build/bin/ToolSuite.ReminderChecks/release/ToolSuite.ReminderChecks.dll artifacts/reminder-editor-checks/new-run
& $sdk artifacts/reminder-editor-build/bin/ToolSuite.SuiteChecks/release/ToolSuite.SuiteChecks.dll artifacts/reminder-editor-suite-checks/new-run --unified-settings
```

新版应用为 `artifacts/reminder-editor-build/bin/ToolSuite.App/release/ToolSuite.exe`。当前旧实例需正常退出后才能启动新版；此次未强制终止运行中的套件。检查与构建产物均不提交。

## 覆盖行为

- 初始为空、多项独立到期、停用项不调度；跳过多个周期只产生一次待确认。
- 确认时开始下一轮；改名保持剩余时间，改间隔与重新启用从完整间隔开始。
- 停用及删除移除待确认项；退出后停止，再次启动恢复配置并重计时。
- 写入与删除保存失败保留配置与原计时；损坏文件明确报错并保留原文。
- 秒／分钟／小时换算、非法数值在真实表单内提示；离线编辑不隐式启动模块。
- 真实托盘新增命令在停用时可用，只打开独立编辑窗口，不创建统一设置；重复新增保留草稿，取消不创建数据；保存失败保留窗口，保存成功关闭。
- 设置页只包含管理列表与入口按钮，不再包含名称／间隔输入；编辑同样使用独立窗口。
- 真实短间隔调度仅创建一个置顶、`ShowActivated=false` 的聚合窗口；逐项确认与关闭全部确认；再次到期重建一个窗口，停用释放窗口。
- 提醒为零或所有项目停用时，不运行模块调度计时器；设置控件卸载时停止界面刷新并退订事件。

## 视觉与辅助功能

已查看真实 WPF 渲染的浅深管理区及弹窗，另生成 240 DIP 紧凑布局、200% 文字编辑布局预览。控件至少 32 DIP 高；文字换行、状态文字、辅助功能名称和焦点样式均已落地。记录中的 200% 检查模拟相同的布局缩放，不修改用户系统文字设置。

| 文字／背景 | 对比度 |
| --- | --- |
| 浅色次文字 #63636A／白色卡片 | 5.96:1 |
| 浅色次文字 #63636A／画布 | 5.47:1 |
| 浅色动作 #0066CC／白色卡片 | 5.57:1 |
| 浅色动作 #0066CC／悬停背景 | 4.56:1 |
| 深色次文字 #A0A0AC／卡片 | 7.10:1 |
| 深色次文字 #A0A0AC／画布 | 7.60:1 |
| 深色动作 #A7A4F0／卡片 | 8.05:1 |

预览已保存到 `docs/images/reminders-*.png`，为示例提醒数据，见[界面设计及复核](../interval-reminders-design.md)。

## 人工验证边界

真实锁屏／解锁、睡眠／休眠、远程会话切换、独占全屏、多显示器混合 DPI、屏幕阅读器与高对比度切换未在本轮实际执行。跨周期时间由可控计时源覆盖，呈现前检查输入桌面并监听会话变化；不能以模拟计时或 `ShowActivated=false` 属性检查代替真实系统环境验证。

计时采用原生 `GetTickCount64`，包含睡眠／休眠时间，依据 [Microsoft Windows Time](https://learn.microsoft.com/windows/win32/sysinfo/windows-time)。显式调用 Win32 可避免未来 .NET 对 `Environment.TickCount64` 的语义变更影响本需求，相关变更见 [Microsoft .NET 兼容性说明](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/environment-tickcount-windows-behavior)。

本轮完成源码、文档与界面预览；未安装、升级或替换用户正在使用的套件，也未重新构建安装包。
