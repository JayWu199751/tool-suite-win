# 托盘菜单点击外部收起验证记录

日期：2026-10-25。使用用户目录的 .NET 10 SDK，检查使用 `artifacts/` 下的隔离数据。

## 问题与原因

托盘右键菜单打开后，点击桌面、其他应用窗口或任务栏，菜单不收起，只能靠再点一次菜单项或再次右键托盘图标关闭。
WPF 弹出层的失活收起机制依赖前台归属：从 `NotifyIcon` 弹出的菜单进程没有前台，点击落进别的窗口时
弹出层根本收不到失活通知。Clipboard 模块的 `TrayIconHost` 早已用 `SetForegroundWindow` 解决过同一问题，
套件托盘 `TrayHost`/`TrayMenu` 缺这一步。

## 修复

- `TrayMenu.OnOpened`：菜单打开后立即把前台转到弹出层自己的 HWND（`PresentationSource.FromVisual` 取
  `HwndSource.Handle`），点击菜单外任意位置即可触发系统级收起；Esc 收起随焦点免费获得。
- `TrayHost`：右键按下时记录 `TickCount64`，弹菜单前比对菜单最近收起时刻——收起与按下几乎同时
  （含 100 毫秒消息乱序容差）说明菜单正是被这记点击顶下去的，这记右键只算收起、不重开，
  避免出现"关了又立刻弹回"的闪动。程序化打开与早已收起的右键不受影响。

## 自动检查

| 检查 | 结果 |
| --- | --- |
| `dotnet build tests/ToolSuite.SuiteChecks -c Release` | 通过，0 警告、0 错误 |
| 完整 `ToolSuite.SuiteChecks` | 未通过：被既有环境问题挡在本功能断言之前——托盘图标尺寸断言得到 24 像素、系统 `SmallIconSize` 为 28 像素，与 `docs/validation/interval-reminders.md` 记录的阻断相同，本次未修改 `LoadTrayIcon` 也未放宽该断言 |
| 隔离验证程序（`artifacts/tray-dismiss-verify`，借用 `ToolSuite.SuiteChecks` 程序集名访问 internal 入口） | 通过：真实 `TrayHost` 打开菜单后前台窗口句柄与弹出层句柄一致（`popup=0x30254 foreground=0x30254`）；防闪重开纯函数六例全部符合预期 |

`HostIntegrationChecks` 已新增与隔离验证程序相同的断言（前台必须落在弹出层、六个防闪重开判定用例），
待图标尺寸环境问题解决后随完整检查生效。

## 可复现命令

```powershell
$sdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
& $sdk build tests/ToolSuite.SuiteChecks -c Release --nologo
& $sdk tests/ToolSuite.SuiteChecks/bin/Release/net10.0-windows/ToolSuite.SuiteChecks.dll artifacts/tray-dismiss-checks/new-run
# 完整检查被图标尺寸断言挡住时，单独验证本功能：
& $sdk run --project artifacts/tray-dismiss-verify -c Release -- artifacts/tray-dismiss-verify
```

`artifacts/` 不入库，隔离验证程序属临时产物。

## 手工验证清单（真实鼠标，未执行）

| 场景 | 预期 |
| --- | --- |
| 右键托盘打开菜单，左键点击桌面空白处 | 菜单收起 |
| 菜单打开，点击其他应用窗口 | 菜单收起 |
| 菜单打开，点击任务栏空白处 | 菜单收起 |
| 菜单打开，按 Esc | 菜单收起 |
| 菜单打开，再次右键托盘图标 | 只收起，不出现"关了又弹回"；再右键可重新打开 |
| 菜单打开，左键单击托盘图标 | 菜单收起；左键本身仍无其他动作 |
| 展开某工具的二级子菜单后点击外部 | 整组菜单一起收起 |

## 已知边界

- 菜单收起后 100 毫秒容差内的右键会被视为"同一记点击"只算收起：用 Esc 关掉菜单后立刻（100 毫秒内）
  右键的第一下不会重开菜单，再点一次即可。收起远早于按下（包括长按托盘图标的任意时长）不受影响。
- 前台激活依赖系统授权：从终端或桌面正常启动的进程可以拿到；若被安全策略拒绝，菜单仍可打开，
  只是退回旧的"点外部不收起"行为，不产生崩溃或错误提示。
