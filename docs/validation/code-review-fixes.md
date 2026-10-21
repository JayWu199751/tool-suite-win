# 代码审查与修复记录（合并后）

日期：2026-10-21。分支：`fix/code-review`，基线：`integration/tool-suite` @ `e0ac68b`。
修复范围只在本工作树内，未改主工作目录与原三个应用的项目目录。

## 1. 审查方法与两条轴

- **规格轴**：逐条对照 [spec.md](../spec.md)（失败回滚保留旧安装、一套主题与只有套件一个自启开关、
  保留系统高对比度与文字大小适配）、[模块接入约定](../module-integration.md)（唯一组合根、契约文案、
  数据根）、[工单 08](../validation/ticket-08.md) 的安装/升级语义。
- **标准轴**：仓库既有约定——`TreatWarningsAsErrors` 零警告、模块样式局部应用、不新增内存门槛/基线/
  节省数值宣称、模块标识只在组合根出现一次、状态文案与公开契约一致。

判定分四类：**缺陷**（用户可见错误行为）、**一致性改进**（行为不变，消除重复/漂移风险）、
**误报**、**按规格保留**。

## 2. 发现、判定与处理

| # | 发现 | 轴 | 判定 | 处理 |
| --- | --- | --- | --- | --- |
| A | 安装器在 `Directory.Move(staging, install)` 之后就丢弃旧安装，随后才写卸载登记与快捷方式；这两步抛异常时用户看到失败，旧安装已删、新安装残留、登记可能半写（`tools/ToolSuite.Installer.Core/InstallerEngine.cs`） | 规格 | 缺陷 | 把“替换目录 + 写卸载登记 + 写快捷方式”纳入同一事务，失败时清理半写登记/快捷方式、恢复旧目录并按旧版本重建登记与快捷方式；文件恢复失败保留原始异常并写明残留位置 |
| B | 性能条设置窗仍可用主题与开机自启，写回模块 `settings.json` 并经 `StartupShell.UpdateSettings` 应用，形成规格禁止的第二权威；对应测试用 `IsVisible`（窗口未 Show 时恒为 false）属无效断言 | 规格 | 缺陷 | `SettingsWindow` 增加“由套件托管”开关（默认托管），托管时两行 `Collapsed`、控件不回写；`PerfMonitorModule` 在套件路径强制 `Theme = EffectiveTheme`、`Autostart = false`，并在启动时归一旧字段；测试改为断言行 `Visibility` 与落盘值 |
| C | 剪贴板只在高对比度与明暗之间二选一，全局无 `HighContrast` 引用，系统高对比度下仍用普通配色 | 规格 | 缺陷 | 新增 `Themes/HighContrast.xaml`（与明暗键集合完全一致，取值来自 `SystemColors`）、纯函数 `ClipboardThemeSelection`、运行期订阅 `SystemParameters.StaticPropertyChanged` 并在 `StopAsync` 退订 |
| D | 安装器两处硬编码 `%LOCALAPPDATA%\ToolSuite` 文案 | 标准 | 一致性改进 | 改用 `SuiteIdentity.DataFolderName` 组合，用户可见文案不变 |
| E | 剪贴板模块版本 `1.0.1` 与 `SuiteIdentity.Version`、README、安装包 `1.0.0` 不一致 | 标准 | 缺陷（版本漂移） | `modules/Clipboard/Directory.Build.props` 改为 `1.0.0`；构建产物 FileVersion 与 ProductVersion 已核对 |
| F | 模块标识在 `SuiteController`、`SuiteSettings` 默认值与补全列表三处重复 | 标准 | 一致性改进 | `ToolSuite.Core/ToolIds.cs` 成为单一真源（Id + 显示名 + 顺序），两处引用它 |
| G | 托盘短状态与设置详情各写一份 `ToolState` 映射，且托盘“不可用”与契约文本不一致 | 标准 | 一致性改进 | `ToolSuite.Core/ToolStateText.cs` 单一映射：短标签供托盘组标题（保持“不可用”这一既有简短文案），详情保留契约“尚未接入，此版本不可用；启用偏好已保留” |
| H | `modules/PerfMonitor/src/PerfMonitor.Core/StartupShell.cs` 的旧提权链路（`StartupAccessContext`/`HandOffToElevatedInstance`） | 规格 | 按规格保留 | 生产路径已不调用，仅契约测试覆盖；保留原实现、不接线 |
| I | 四份各自实现的原子写入（`SuiteSettings`、便签 `DataStore`、`WindowsSettingsStore`、剪贴板 `JsonStore`） | 标准 | 按规格保留 | 分属不同模块存储层，写入穿透/替换策略不同；统一抽取会把模块耦合进宿主，收益低于风险 |
| J | `PerformanceBarViewModel` 74 个字段、`ClipboardModule.cs` 619 行、`ReloadEntries` 重复调用 | 标准 | 按规格保留 | 属既有模块内部结构，重构超出本次合并与审查修复范围 |
| K | 温度无独立开关 | 规格 | 误报 | 原产品 `MetricVisibility` 只有五项，温度查询由 CPU 指标门控、GPU 查询由 GPU 指标门控（`PerformanceSlowMetricsSampler.cs:141-143`），已满足“关闭相关指标后不执行无用查询” |
| L | 剪贴板 `settings.json` 仍持久化 `theme`/`autoStart` 旧字段 | 规格 | 按规格保留 | 字段存在但不参与决策（托管后不再生效）；删除会破坏既有设置文件兼容与对应测试 |
| M | 生产 EXE 的 `--verify-host` 分支 | 标准 | 按规格保留 | `scripts/verify-host.ps1` 与 `docs/validation/` 依赖它做宿主基础设施验证，属既定验证入口 |

## 3. 红 → 绿记录（TDD）

每个修复先写会失败的检查，再改实现；没有放宽、跳过或删除既有断言。

1. **A 安装器事务**
   - 红：新增“升级写卸载登记失败”“全新安装写快捷方式失败”“回滚自身失败”三条检查，先失败于
     `升级失败必须把旧版本文件恢复到原安装目录`（旧实现已删除 `previous`）。
   - 绿：`InstallerEngine.Install` 记录 `installed/completed` 状态，失败进入 `RollBack`；文件恢复失败抛
     `InvalidOperationException`（内部异常为原始异常，消息含残留位置）。三条检查通过。
   - 复核补强（第二轮）：回滚只恢复文件会让失败升级后的“已安装的应用”条目消失，属修复自身留下的
     退化。先收紧断言至“卸载登记与快捷方式按旧版本还原”（红：`升级失败必须把卸载登记恢复为旧安装`），
     再在 `RollBack` 中按 `version.txt` 记录的旧版本重建登记与快捷方式，并把“文件恢复失败”
     （回滚未完成）与“登记/快捷方式还原失败”（次生失败）分开上报；失败注入改为一次性瞬时写入错误，
     与“登记持续不可写”区分。17 条安装器检查通过。
2. **B 性能条设置托管**
   - 红：新增 `SettingsWindow(..., suiteManaged: false)` 用例与“套件运行路径落盘”用例，先以
     `SettingsWindow 没有名为 suiteManaged 的参数` 失败；随后把原 `IsVisible` 断言改为两行
     `Visibility` 断言（`IsVisible` 在未 Show 的窗口上恒为 false，属无效断言）。
   - 绿：托管开关 + 控件级拒绝回写 + 模块侧强制主题/自启口径 + 启动归一。
3. **C 剪贴板高对比度**
   - 红：`ClipboardUiChecks` 新增 `ThemeScenario`，因 `ClipboardThemeSelection` 不存在而编译失败。
   - 绿：新增字典与纯函数后通过；同时校验三套字典键集合与设计 token（19 个 `Brush.*`）完全一致
     ——`Font.Sans`/`Font.Mono` 由 `ApplyTheme` 在运行期注入，不属于字典键，故字典键为 19 个。
4. **F/G 单一真源**
   - 红：新增 `ContractSingleSourceChecks`（`ToolIds`/`ToolStateText` 不存在 → 编译失败）。
   - 绿：补充 `ToolIds`/`ToolStateText` 后通过；该检查的期望值取自公开契约，用于防止再次漂移。
   - 说明：F/G 是行为不变的去重，“红”体现为单一真源缺失（编译失败），不是行为断言失败；
     既有全部 SuiteChecks 继续通过。
5. **D/E**：文案与版本属常量收敛，由构建产物版本号与安装器检查（“安装到套件自己的临时目录”）
   间接验证，未新增专门检查。

## 4. 验证证据（实际数字）

在 `.worktrees/fix-review` 根执行，隔离数据目录位于 `artifacts/`。

| 命令 | 结果 |
| --- | --- |
| `dotnet build ToolSuite.slnx -c Release` | 成功，**0 警告 0 错误** |
| `dotnet test modules/Clipboard/ClipboardTool.Tests -c Release --no-build --filter "Category!=Desktop"` | **481 通过**，0 失败（基线 481） |
| `dotnet test modules/PerfMonitor/tests/PerfMonitor.Core.Tests -c Release --no-build` | **161 通过**（基线 161） |
| `dotnet test ... PerfMonitor.Windows.Tests -c Release --no-build` | **50 通过**（基线 50） |
| `dotnet test ... PerfMonitor.App.Tests -c Release --no-build` | **24 通过**（基线 22，新增 2 条托管检查） |
| `dotnet tests/ToolSuite.HostChecks/bin/Release/net10.0/ToolSuite.HostChecks.dll <临时目录>` | **4 PASS**，退出码 0 |
| `dotnet tests/ToolSuite.ClipboardChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardChecks.dll <临时目录>` | **8 PASS**，退出码 0 |
| `dotnet tests/ToolSuite.ClipboardUiChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardUiChecks.dll <临时目录>` | **5 PASS**（基线 4，新增主题字典检查），退出码 0 |
| `dotnet tests/ToolSuite.SuiteChecks/bin/Release/net10.0-windows/ToolSuite.SuiteChecks.dll <临时目录>` | **15 项 + 1 汇总 = 16 行 PASS**（基线 14 条，新增 1 条单一真源检查），退出码 0 |
| `dotnet tests/ToolSuite.LifecycleChecks/bin/Release/net10.0-windows/ToolSuite.LifecycleChecks.dll <临时目录>` | **41 项 + 5 组汇总 = 46 行**，退出码 0（工单 07 记录为 40 条；该检查程序未改动，属既有计数口径差异） |
| `dotnet tests/ToolSuite.InstallerChecks/bin/Release/net10.0-windows/ToolSuite.InstallerChecks.dll <临时目录>` | **16 项 + 1 汇总 = 17 行 PASS**（基线 13 条，新增 3 条回滚检查），退出码 0 |
| `dotnet modules/Pinto/tests/Pinto.Checks/bin/Release/net10.0/Pinto.Checks.dll <临时目录>` | **24 项通过**（另 1 行结果目录摘要），退出码 0 |
| `dotnet .../Pinto.ModuleChecks.dll <临时目录>` | **20 项通过**（另 1 行汇总），退出码 0 |
| `dotnet .../Pinto.DesktopChecks.dll <临时目录>` | **77 项通过**，退出码 0（基线 77） |
| `./scripts/verify-host.ps1`（`DOTNET_ROOT` 指向 10.0.402 SDK） | 通过，输出“通过，隔离检查产物：artifacts/host-check-…”，`verification-passed.txt` 已生成 |
| 产物版本核对 | `ClipboardTool.dll`、`ToolSuite.dll` 的 FileVersion 均为 `1.0.0.0`，ProductVersion `1.0.0+<sha>` |

未执行 `scripts/build-installer.ps1`（打包由协调方在合并后执行），未写真实开机启动项，
未触碰 `%LOCALAPPDATA%\ToolSuite`。

### 4.1 协调方独立复验与其间的一次环境干扰

合并前由协调方在同一工作树独立重跑全部入口，结果与上表一致（构建 0 警告 0 错误；481 / 161 / 50 / 24；
HostChecks 4、ClipboardChecks 8、SuiteChecks 16 行、LifecycleChecks 46 行、InstallerChecks 17 行、
Pinto 24 / 20 / 77）。

首轮批量运行时出现过两次非确定性失败，已定位为环境干扰而非实现缺陷，未改断言、未加跳过：

- `ClipboardUiChecks` 失败于 `real global shortcut summons`，`SuiteChecks` 失败于
  `停用工具应能打开离线设置`。
- 当时系统中存在一个 21:00 启动的遗留 `ClipboardTool.exe` 进程（前一轮验证留下的实例，
  仍可能占用 `Ctrl+Shift+V` 并影响前台窗口与离线设置窗口的创建）。
- 处置：同一工作树按原顺序复跑两次，两项均通过（`ClipboardUiChecks` 5 PASS、`SuiteChecks` 16 行 PASS）；
  另在独立基线工作树（`e0ac68b`）上复跑 `SuiteChecks` 亦通过，确认与本次改动无关。
- 结论：这两项检查对残留的热键占用与前台窗口敏感，复验前应确认没有遗留 `ClipboardTool.exe`
  / `ToolSuite.exe` 进程；该敏感性属既有检查设计，不在本次修复范围内。

## 5. 已知影响与取舍

- **A**：失败的升级会恢复旧安装的文件、版本号、卸载登记与快捷方式，用户看到的是旧版本仍可用；
  仅当登记本身持续不可写（如注册表被策略锁定）时，“已安装的应用”条目可能缺失，旧文件仍已恢复，
  重新安装或再次升级一次即可补齐登记。已在[交付说明](../handoff.md)写明。
- **B**：性能条 `settings.json` 里的 `theme`/`autostart` 仍保留为兼容字段，启动时归一为套件有效主题
  与关闭自启；用户可见变化是设置窗不再有这两行。
- **C**：高对比度字典会随系统高对比度切换重新挂载；未在字典里写死任何品牌色，避免成为第二套视觉设计。
- **F/G**：对外字符串、托盘组标题与设置详情文本保持原样（“不可用”仍是短标签，详情仍含契约文本）。

## 6. 未执行、因而不视为通过的真实场景

- 真实 UAC 提权/拒绝、真实登录自启动、干净机器安装与卸载。
- 真实系统高对比度切换的视觉观感（自动检查只验证字典键集合与选择函数；切换订阅已实现但未在
  真实高对比度主题下目视确认）。
- 多显示器/DPI 摆放、真实鼠标贴边、真实管理员窗口粘贴与真实按键演示。
- `Category=Desktop` 的桌面类剪贴板用例（无交互桌面会话下不执行，未改断言、未加跳过）。

以上任一项未经人工复验，均不能以本文的自动检查通过代替。
