# 工单 09 验证：整套功能回归与交付说明

日期：2026-10-21。分支 `ticket/09-regression`，基于集成提交 `db8b79d`（工单 07、08 已并入）。
全部命令在工作树 `C:/Users/10854/Code/tool-suite/.worktrees/09-regression` 根执行，
SDK 固定为 `C:/Users/10854/AppData/Local/Microsoft/dotnet/dotnet.exe`（10.0.402）。
所有检查使用 `artifacts/` 下的隔离数据根；未读写日常套件数据、真实开机启动项或原三个应用仓库。

## 结论摘要

- `dotnet build ToolSuite.slnx -c Release`：**0 警告、0 错误**。
- 安装包用最终源码重建并通过真实静默安装/卸载冒烟：`artifacts/installer/ToolSuite-Setup-1.0.0.exe`。
- 除 `WindowSummonOrderTests` 两条桌面 z 序用例外，其余检查全部通过；该两条与集成基线一致，
  归因于当前会话无真实交互桌面（见“失败归因”），**未改写为通过**。
- 未发现需要修复的真实缺陷；本轮只新增文档，未改产品代码、脚本与测试断言。
- 交付说明：[docs/handoff.md](../handoff.md)。

## 一、最终产物重建与安装包冒烟

| 命令 | 结果 |
| --- | --- |
| `./scripts/build-installer.ps1`（`DOTNET_ROOT` 指向 SDK） | 成功：自包含发布 + 单文件安装包 |
| `./scripts/verify-installer.ps1 -Smoke` | 13 条安装器隔离检查全部 PASS，真实安装包静默安装/卸载冒烟通过 |

产物：

- 安装包：`artifacts/installer/ToolSuite-Setup-1.0.0.exe`
  - 大小：125,905,755 字节（120.1 MB）
  - SHA-256：`423DDABF127C09D03846250DAFFF684A776F348F84A69D8F064983D6D597793D`
  - 构建时间：2026-10-21T20:14:43+08:00（`artifacts/installer/build-info.txt`）
- 发布目录：`artifacts/publish/win-x64`（含 `coreclr.dll`、`hostfxr.dll`、`PresentationFramework.dll`）
- 载荷：`artifacts/publish/ToolSuite-win-x64.zip`
- `ToolSuite.runtimeconfig.json` 仅含 `includedFrameworks`，不依赖目标机预装运行时。

`-Smoke` 实际执行的安装/卸载验证（临时目录 `artifacts/installer-smoke`，未写开机启动项）：

1. 静默安装退出码 0，安装目录含 `ToolSuite.exe`、`coreclr.dll`、`PresentationFramework.dll`、
   `ToolSuite.Setup.exe`、`version.txt`（`1.0.0`）。
2. HKCU 卸载登记 `DisplayName=工具套件`、`InstallLocation` 指向临时安装目录。
3. 安装未写入 `ToolSuite.Suite` 开机启动值。
4. 静默卸载退出码 0，安装目录、卸载登记与冒烟根均被清理，未创建快捷方式。
5. 脚本前置检查确认无既有套件卸载登记/启动值/快捷方式，未覆盖真实安装状态。

## 二、回归命令与结果

| 检查入口 | 命令（省略 `$d = .../dotnet.exe`） | 结果 |
| --- | --- | --- |
| 解决方案构建 | `$d build ToolSuite.slnx -c Release` | **0 警告、0 错误** |
| ClipTool 单元/集成 | `$d test modules/Clipboard/ClipboardTool.Tests -c Release --no-build` | **481 通过 / 2 失败**（483，`WindowSummonOrderTests`，见失败归因） |
| 同上，排除桌面类 | `$d test modules/Clipboard/ClipboardTool.Tests -c Release --no-build --filter "Category!=Desktop"` | **481/481 通过** |
| 性能条 Core | `$d test .../PerfMonitor.Core.Tests -c Release --no-build` | **161 通过** |
| 性能条 Windows | `$d test .../PerfMonitor.Windows.Tests -c Release --no-build` | **50 通过** |
| 性能条 App | `$d test .../PerfMonitor.App.Tests -c Release --no-build` | **22 通过** |
| 便签数据 | `$d modules/Pinto/tests/Pinto.Checks/bin/Release/net10.0/Pinto.Checks.dll artifacts/regress/pinto-checks` | **24 项通过**，退出码 0 |
| 便签模块 | `$d .../Pinto.ModuleChecks/bin/Release/net10.0-windows/Pinto.ModuleChecks.dll artifacts/regress/pinto-module2` | **20 条通过**，退出码 0 |
| 便签桌面 | `$d .../Pinto.DesktopChecks/bin/Release/net10.0-windows/Pinto.DesktopChecks.dll artifacts/regress/pinto-desktop` | **77 条通过**，退出码 0 |
| 宿主控制入口 | `$d tests/ToolSuite.HostChecks/bin/Release/net10.0/ToolSuite.HostChecks.dll artifacts/regress/host` | **4 条 PASS**，退出码 0 |
| 剪贴板领域 | `$d .../ToolSuite.ClipboardChecks.dll artifacts/regress/clip` | **8 条 PASS**，退出码 0 |
| 剪贴板 UI | `$d .../ToolSuite.ClipboardUiChecks.dll artifacts/regress/clip-ui` | **4 条 PASS**，退出码 0 |
| 整套控制 | `$d .../ToolSuite.SuiteChecks.dll artifacts/regress/suite` | **14 条检查 PASS**（另 1 条总结行），退出码 0 |
| 生命周期 | `$d .../ToolSuite.LifecycleChecks.dll artifacts/regress/lifecycle` | **40 条通过**（5 组总结 PASS），退出码 0 |
| 安装器/自启 | `$d .../ToolSuite.InstallerChecks.dll artifacts/regress/installer` | **13 条检查 PASS**（另 1 条总结行），退出码 0 |
| 宿主整体脚本 | `./scripts/verify-host.ps1`（`DOTNET_ROOT` 指向 SDK） | 通过，生成 `verification-passed.txt` |

两次安装器入口（`verify-installer.ps1` 内置调用与单独复跑）均为同一 13 条检查，结果一致。

## 三、失败归因

**`WindowSummonOrderTests` 两条失败（环境限制，不是本次引入的缺陷）**

- 失败用例：`被遮挡的面板呼出后实际在前且不抢焦点(panelStillMarkedTopmost: True / False)`。
- 失败断言：`Assert.Equal()` 比较 `WindowFromPoint` 命中的窗口句柄，实际命中遮挡窗口
  （期望 11864270/11929806，实际 11208468/11274004），即面板未被判定为最前。
- 归因依据：
  1. 该用例 `Category=Desktop`，依赖真实交互桌面的顶层 z 序与前台窗口；
     当前会话由自动化进程驱动，桌面被其他窗口覆盖，属已知不稳定的桌面条件。
  2. 本轮单独复跑该用例一次（`--filter "FullyQualifiedName~WindowSummonOrderTests"`）：
     **0 通过 / 2 失败**，`SetWindowPos` 返回成功但 `WindowFromPoint` 仍命中遮挡窗口；
     同一次运行中“遮挡窗口先被命中”的前置断言通过，说明窗口创建与命中测试正常，
     差异只出现在置顶 z 序变化是否被当前会话采纳。
  3. 工单 03 记录：同一用例在基线 `56a9206` 上同样失败，6 次单独重跑 1 次通过；
     工单 07 在 `ecfbe76` 上复现同样 2 条失败。
  4. 排除桌面类后 481/481 全部通过，说明非桌面规则、存储、热键与粘贴链路未受影响。
- 处理：未放宽断言、未加跳过标记、未改判为通过；在交付中标注为未通过并给出复验步骤（见第五节）。
  若在真实交互桌面复验仍失败，则按真实缺陷调查 `WindowPlacer.PlaceSummonDip` 的置顶重入逻辑。
- 复验步骤（真实交互桌面）：登录本机桌面、关闭全屏/远程会话，运行
  `dotnet test modules/Clipboard/ClipboardTool.Tests -c Release --no-build --filter "FullyQualifiedName~WindowSummonOrderTests"`，
  期望 2/2 通过；若仍失败，则升级为真实缺陷调查（面板 `SetWindowPos` 与 topmost 恢复逻辑）。

## 四、覆盖矩阵

“检查入口”为实际执行的项目/脚本；“未执行的人工场景”列只列该领域尚未真实运行的部分。

| 功能领域 | 检查入口 / 结果 | 未执行的人工场景（复验方式见第五节） |
| --- | --- | --- |
| 唯一实例、唯一托盘、左键无动作、右键分组、无常驻首页 | `ToolSuite.HostChecks`（4 PASS）、`SuiteChecks` 第 4 条（托盘分组与无呼出动作）、`verify-host.ps1`（`--verify-host`：空白启动无首页、重复实例通知原实例） | 真实鼠标左键/双击与右键菜单外观 |
| 统一设置按需创建、关闭释放、失败重试 | `SuiteChecks` 第 6、13、14 条；`verify-host.ps1`（设置窗关闭后窗口数 0） | 无（窗口生命周期为真实 WPF 验证）；非默认 DPI 下视觉确认未做 |
| 统一主题、便签强调色隔离、高对比度托盘 | `SuiteChecks` 第 1、2、8 条（主题去重、单模块故障隔离、便签强调色不受串扰） | 系统级高对比度、系统文字大小切换的视觉确认 |
| 工具启停、状态持久化、隐藏≠停用、全部停用可恢复 | `SuiteChecks` 第 3、5、7、9、10 条；`LifecycleChecks` 便签/性能条/剪贴板停用后无窗口、可重启 | 真实托盘点击启停（检查调用托盘同一命令） |
| 保存失败不丢编辑、失败不伪装成功、退出前预检 | `SuiteChecks` 第 11、12 条；`LifecycleChecks` 保存失败保留窗口与草稿；`HostChecks` 启用落盘失败回滚 | 无（真实文件锁制造失败） |
| 重复启停无重复监听/迟到更新/影响其他工具 | `LifecycleChecks`（重复显隐、重建后在途结果作废、整套重复启停）、`SuiteChecks` 第 5 条 | 长时间反复启停的压力观察未做（规格不要求） |
| 剪贴板文字/图片记录、去重、上限、搜索、置顶、备注、撤销、清空 | `ClipboardTool.Tests` 481 通过；`ClipboardChecks` 8 PASS；`ClipboardUiChecks` 4 PASS | 真实系统剪贴板截图入库（`Win+Shift+S`） |
| 呼出快捷键 Ctrl+Shift+V、改键、冲突、停用不响应 | `ClipboardTool.Tests`（默认键、AccelCodec）、`ClipboardUiChecks`（呼出/收起、冲突、离线改键、停用不记录） | 真实按键与新组合的人工演示 |
| 普通/管理员窗口粘贴、失败反馈 | `ClipboardUiChecks`（Enter 写入剪贴板、无焦点快照时失败反馈） | 管理员窗口实际粘贴（需 UAC + 真实前台窗口） |
| 性能条指标、缺失显示、只查需要的数据源、全屏显隐、隐藏暂停采样 | `PerfMonitor.Core.Tests` 161、`Windows.Tests` 50、`App.Tests` 22；`LifecycleChecks` 性能条隐藏暂停时钟/设置窗重建 | 真实 GPU/温度硬件缺失组合、真实全屏程序下的自动隐藏 |
| 性能条多显示器/DPI、拖动、贴靠 | `PerfMonitor.App.Tests`、`Pinto.DesktopChecks` 中的布局用例 | 真实多显示器拔插与缩放切换 |
| 便签清单编辑/排序/完成/删除、排序与贴边、隐藏重建 | `Pinto.Checks` 24、`Pinto.ModuleChecks` 20、`Pinto.DesktopChecks` 77、`LifecycleChecks` 便签场景 | 真实鼠标拖动贴边与跨屏贴边 |
| 便签手动导出/导入、损坏恢复 | `LifecycleChecks` 导入对话框按需创建/关闭/重建；`Pinto.Checks` 数据与备份用例；`SuiteChecks` 保存失败与重试 | 真实系统文件选择器的完整导入/导出（含取消选择） |
| 开机启动默认关闭、只写套件启动值、失败反馈、只启动已启用工具 | `InstallerChecks` 13 PASS（含真实便签模块登录启动可见性）；`verify-installer.ps1` | 真实登录/重启后由 HKCU Run 拉起 |
| 安装、升级、卸载、文件占用拒绝、保留数据 | `InstallerChecks`（安装/升级占用回滚/卸载保留数据）；`verify-installer.ps1 -Smoke`（真实安装包静默安装/卸载） | 真实旧版本覆盖升级、系统“应用和功能”界面卸载 |
| 自包含、无预装运行时、单文件安装包 | `verify-installer.ps1` 校验 `runtimeconfig.json` 与产物完整性；`-Smoke` 实际解包运行文件检查 | 干净 Windows 11 无 .NET 运行时机器上的启动 |
| 管理员权限与拒绝 UAC | 代码清单 `requireAdministrator`（人工审查）；`verify-host.ps1` 经 `dotnet ToolSuite.dll` 运行，不触发 UAC | 真实 UAC 允许/拒绝的点击 |
| 资源释放、退出后无文件锁、无残留工作 | `LifecycleChecks`（三类数据文件可独占打开、重复启停）、`verify-host.ps1`（安全退出） | 操作系统级句柄枚举、GPU 查询子进程退出观察 |
| 原应用与原数据独立 | `InstallerChecks`（可控 Run 存储与内存注册表模拟旧启动项，安装/卸载不触碰）、发布与安装路径审查 | 本机真实旧应用数据/启动项的完整性核对（未读取真实数据） |
| 内存表现 | 规格要求不做专项测量；仅有功能性释放检查（`LifecycleChecks`） | 任何内存数值对比均未执行，交付不报告节省 MB 或百分比 |

## 五、未执行的人工场景与复验步骤

以下场景需要真实 UAC、交互桌面、登录会话、特定硬件或干净机器，**未执行，不标记为通过**。

1. **UAC 允许/拒绝正式 EXE**
   步骤：资源管理器双击安装目录的 `ToolSuite.exe` → 分别点“是”和“否”。
   期望：点是 → 一个进程、一个托盘、三个工具按保存状态启动；点否 → 无进程、无托盘、无部分工具运行。
2. **真实登录自启**
   步骤：设置中开启开机启动 → 确认 `HKCU\...\Run\ToolSuite.Suite` 指向安装目录的 `ToolSuite.exe`
   → 注销/重启 → 允许 UAC。期望：只有一个托盘；停用工具未启动；隐藏便签与隐藏性能条保持隐藏。
3. **托盘鼠标交互**
   步骤：物理鼠标左键单击、双击托盘图标；再右键展开三个分组与统一设置。
   期望：左键/双击无任何窗口；分组标题状态正确；剪贴板组无“打开面板”；隐藏便签出现在便签组。
4. **系统“应用和功能”卸载**
   步骤：设置 → 应用 → 已安装的应用 → 工具套件 → 卸载。
   期望：程序与卸载登记消失，`%LOCALAPPDATA%\ToolSuite` 保留，原应用不受影响。
5. **真实旧版本覆盖升级**
   步骤：安装 1.0.0 并保持运行 → 运行新版本安装包，观察拒绝提示 → 退出套件后点“重试”。
   期望：运行中被拒绝且旧版本可用；退出后升级成功；数据与自启状态保留。
6. **干净机器（无 .NET 运行时）**
   步骤：在未安装 .NET 10 的 Windows 11 x64 上安装并启动。
   期望：直接启动，不提示缺少运行时。
7. **安装器交互界面与 SmartScreen**
   步骤：双击安装包走完向导（含自定义目录、桌面快捷方式勾选）；从浏览器下载后运行。
   期望：界面文案与勾选项生效；SmartScreen 提示可选择继续，安装包未签名属已知限制。
8. **真实截图与管理员窗口粘贴**
   步骤：`Win+Shift+S` 截图 → 呼出面板确认入库；以管理员身份打开记事本/终端 → 选中条目回车。
   期望：截图入历史可粘贴；管理员窗口收到内容；失败时有明确提示。
9. **多显示器/DPI 与辅助功能**
   步骤：性能条与便签拖到副屏，切换 100%/150%/200% 缩放后重启套件；开启系统高对比度与大号文字。
   期望：位置可用、贴边与文字大小正常、托盘与菜单可读、快捷键操作完整。
10. **真实剪贴板 z 序复验**（对应第三节失败）
    步骤：在交互桌面运行 `--filter "FullyQualifiedName~WindowSummonOrderTests"`。
    期望：2/2 通过；仍失败则按缺陷调查面板置顶/前台恢复逻辑。
11. **操作系统级资源观察**
    步骤：启用 GPU 指标时退出套件，检查 `nvidia-smi` 等短命查询子进程、`ToolSuite.exe` 句柄与
    文件锁是否释放。期望：无残留进程、数据文件可被其他程序打开。

## 六、缺陷修复记录

本轮**未发现真实缺陷**，因此没有产品代码改动，也没有放宽或跳过任何断言。
唯一失败项已在第三节说明为环境限制，并按工单要求保留失败记录。

回归中观察到的非缺陷现象，供后续参考：

- `dotnet build` 日志中 `ClipboardTool.Domain`、`ClipboardTool.Application`、
  `ClipboardTool.Infrastructure.Windows` 三个项目打印了 `bin/Debug` 输出行，其余项目均为
  `bin/Release`；构建结果仍为 0 警告 0 错误，且各检查实际引用的 `bin/Release` 产物齐全并通过。
  这是并入前既有的输出路径现象，本轮未改动相关工程文件，也未据此修改任何断言。
- `dotnet test ToolSuite.slnx` 一次性运行会因 WPF 测试宿主并行产生主题静态状态竞争，
  因此按工单要求逐工程运行。

## 七、文档交付

- 新增 [docs/handoff.md](../handoff.md)：安装/升级/卸载与数据保留、独立数据位置、管理员权限与拒绝 UAC、
  托盘左键/右键分组、`Ctrl+Shift+V` 与改键、启停与“隐藏≠停用”、便签导出/导入、统一主题、
  开机启动默认关闭与失败反馈、日常内存观察方式（不含未测量的节省数值）、人工验收清单与已知限制。
- 更新 [README.md](../../README.md)：项目结构、快速开始、逐工程检查命令、去除此前的
  “工具将在后续工单接入/当前界面显示尚未接入”等过时描述。
- 工单 01–09 状态更新为 resolved 并补充实施结果；[tickets/README.md](../../tickets/README.md)
  同步说明最终状态。

## 八、风险

- 两条桌面 z 序用例在无交互桌面会话中失败；它们不影响其余 481 条与整套功能检查，但真实桌面
  复验前不能宣称“全部测试通过”。
- 安装包未签名、体积约 120 MB，首启/安装耗时受杀毒扫描影响。
- 正式套件要求管理员权限，登录自启必然触发 UAC，拒绝即本次不启动。
- 冒烟会真实写入并删除 HKCU 卸载登记；进程被强制中断时可能残留 `Uninstall\ToolSuite`，
  需人工清理（脚本发现已有登记时会拒绝运行）。
- 自动检查以 `dotnet ToolSuite.dll` 运行，未覆盖真实 EXE 的 UAC 路径；该路径只能人工验收。
