# 工单 07 验证：隐藏界面回收与重复重建

日期：2026-10-21。分支 `ticket/07-recycling`，基于集成分支 `ecfbe76`（ticket 03 并入后）。
所有检查使用工作树内 `artifacts/` 下的隔离数据根，不读取套件或原应用的日常数据。
剪贴板检查使用注入的假平台，未读写真实系统剪贴板；性能条检查关闭全部指标，不发起硬件查询。

## 红绿证据

1. **红（启动为隐藏便签建窗）**：先写 `tests/ToolSuite.LifecycleChecks` 的便签场景，并把
   `AppCoordinator.cs`、`NoteWindow.xaml.cs` 暂存回基线后重建运行，首条断言失败：

   ```
   System.Exception: 失败：启动不为隐藏便签创建窗口
      at Program.PintoScenario(...) in tests/ToolSuite.LifecycleChecks/Program.cs:line 76
   ```

   基线在 `InstallWindows()` 里为全部便签（含隐藏）创建 `NoteWindow`，隐藏便签的窗口只是不 `Show()`，
   与验收条件 1 不符。

2. **绿（按需创建与回收）**：改 `AppCoordinator` 为只给 `IsVisible` 的便签 `EnsureWindow`；`ShowNote`
   按需重建窗口；`HideNote` 提交编辑、捕获布局、保存成功后才 `ReleaseWindow`（移出索引 → 关闭 → 断订阅）；
   `HideAll` 成功保存后回收全部窗口。重跑生命周期检查全部通过。

3. **红（保存失败时仍尝试回收）**：实现前若照搬“保存失败只恢复可见标记”的旧路径，窗口会被 `Hide()`
   而非保留。新检查以真实文件独占锁制造保存失败，断言窗口实例仍在、可恢复编辑仍在、故障解除后同一
   入口可再次隐藏并释放。首轮运行因检查自身沿用了“草稿仍在输入框”的假设而失败：
   `失败：保存失败保留窗口与未提交草稿`。原因是 `CommitPendingEdits()` 会把草稿提交进清单（这是
   期望的“不丢编辑”），断言改为“窗口保留 + 清单里存在该草稿”后通过，未放宽实现。

4. **既有检查随语义更新（非实现放水）**：隐藏现在会真实释放窗口，因此：
   - `Pinto.ModuleChecks` 中持有旧窗口引用、以及“隐藏后仍应存在一个隐藏窗口”的断言改为读取
     `show-note` 重建后的当前窗口、并以存档中的 `IsVisible` 判定隐藏状态；
   - `Pinto.DesktopChecks/LayoutChecks`、`DockingChecks` 不再从 `coordinator.Windows` 取隐藏便签的窗口，
     改为检查“隐藏便签无窗口”，并在 `ShowNote` 之后重新获取窗口与原生句柄。
   更新后两个检查均全量通过（DesktopChecks 77 条，ModuleChecks 20 条）。

5. **性能条/剪贴板的既有缺口已由 03/04 关闭**：设置窗关闭释放、隐藏暂停采样与时钟、面板停靠清缩略图与
   在途失效在基线已实现；本工单把这三类行为纳入统一的、可重跑的跨模块检查，未新增第二套实现。

## 新增/扩展的检查

新增 `tests/ToolSuite.LifecycleChecks`（控制台，真实 WPF/STA，退出码非零即失败，已加入 `ToolSuite.slnx`）。
每个场景都经模块/窗口公开入口执行，并用隔离数据根观察结果；不读取私有字段、不做内存测量。

### 便签（19 条）

1. 预置一张隐藏便签（含完成项、位置/尺寸）与一张隐藏贴边便签，启动后 `NoteWindow` 数量为 0。
2. `show-note/<id>` 按需重建：标题、清单文字、完成状态、Left/Top/Width/Height 全部恢复。
3. `hide-all` 保存成功后窗口数量为 0；再次 `show-note` 重建并恢复内容与布局。
4. 在输入框写入草稿后独占锁定 `state.json`，`Window.Close()` 触发隐藏：窗口实例保留、可见、
   草稿已在窗口清单中；解锁后再次关闭，窗口被释放。
5. 重建后草稿已持久化且只有一份（无重复记录）；连续 3 轮显示/隐藏后每次只有一个窗口、清单条数不变。
6. 隐藏的贴边便签重建后仍贴左/上边；再次隐藏/显示后仍贴边。
7. 停用后无 `NoteWindow`，`state.json` 可独占打开；重启后只为可见便签建窗，隐藏便签无窗口。
8. 便签导入对话框：按需 `Show`、显示备份/现有数量，关闭后从 `Application.Windows` 消失；
   重建实例显示新的数量。

### 性能条（11 条）

1. 启动只有一个 `PerformanceBarWindow`；`settings` 命令创建设置窗，改动 GPU 开关后关闭，窗口消失。
2. 重建设置窗是新实例、显示已保存的 GPU 配置、改动继续落盘。
3. 停用后性能条与设置窗全部消失，`settings.json` 可独占打开；重启只重建一个性能条。
4. 手动隐藏/再显示后仍只有一个窗口，且可见状态正确。
5. 仅开启时间指标：可见时 `TimeText` 会变化；隐藏 1.3 秒内保持不变；再显示后恢复更新
   （验证“无可见指标不做时钟更新”，不依赖私有计时器字段）。

### 剪贴板（4 条）

1. 真实 `PanelWindow` + 受控解码队列：呼出后可视图片卡加载缩略图，缓存有条目。
2. `Dock()` 后缓存为 0，所有 `ThumbnailHost` 不再持有位图。
3. 呼出→入队在途解码→收起→执行解码任务：缓存仍为 0，旧窗口不回写位图。
4. 再次呼出可重新加载缩略图（收起不是永久关闭）。

### 整套（6 条）

1. `SuiteController` 同时启动便签、性能条、剪贴板（假平台），三者各一个窗口。
2. `PrepareExitAsync` 后 `StopAsync`：三类窗口全部释放。
3. 便签 `state.json`、性能条 `settings.json`、剪贴板 `settings.json`/`clipboard-history.json`
   均可独占打开（无残留文件锁）。
4. 重复 `StartAsync`/`StopAsync`：窗口数量不重复，退出后仍无窗口。

## 已运行命令与结果

SDK：`C:/Users/10854/AppData/Local/Microsoft/dotnet/dotnet.exe`。均在工作树根执行。

| 命令 | 结果 |
| --- | --- |
| `dotnet build ToolSuite.slnx -c Release` | 成功，0 警告，0 错误 |
| `dotnet tests/ToolSuite.LifecycleChecks/bin/Release/net10.0-windows/ToolSuite.LifecycleChecks.dll artifacts/lifecycle` | 40 条通过，退出码 0（复跑一次仍 40/40） |
| `dotnet modules/Pinto/tests/Pinto.ModuleChecks/bin/Release/net10.0-windows/Pinto.ModuleChecks.dll artifacts/pinto-module` | 20 条通过，退出码 0 |
| `dotnet modules/Pinto/tests/Pinto.DesktopChecks/bin/Release/net10.0-windows/Pinto.DesktopChecks.dll artifacts/pinto-desktop` | 77 条通过，退出码 0 |
| `dotnet modules/Pinto/tests/Pinto.Checks/bin/Release/net10.0/Pinto.Checks.dll artifacts/pinto-checks` | 24 项数据检查通过，退出码 0 |
| `dotnet tests/ToolSuite.ClipboardChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardChecks.dll artifacts/clip-checks` | 8 条 PASS，退出码 0 |
| `dotnet tests/ToolSuite.ClipboardUiChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardUiChecks.dll artifacts/clip-ui` | 4 条 PASS，退出码 0 |
| `dotnet tests/ToolSuite.HostChecks/bin/Release/net10.0/ToolSuite.HostChecks.dll artifacts/host-checks` | 4 条 PASS，退出码 0 |
| `dotnet test modules/PerfMonitor/tests/PerfMonitor.Core.Tests -c Release --no-build` | 161 通过 |
| `dotnet test modules/PerfMonitor/tests/PerfMonitor.App.Tests -c Release --no-build` | 22 通过 |
| `dotnet test modules/PerfMonitor/tests/PerfMonitor.Windows.Tests -c Release --no-build` | 50 通过 |
| `dotnet test modules/Clipboard/ClipboardTool.Tests -c Release --no-build` | 481 通过 / 2 失败（`WindowSummonOrderTests`，见风险；基线同样失败） |

可重跑命令（每次使用新的隔离根，`artifacts/` 已 gitignore）：

```bash
DOTNET=C:/Users/10854/AppData/Local/Microsoft/dotnet/dotnet.exe
cd C:/Users/10854/Code/tool-suite/.worktrees/07-recycling
$DOTNET build ToolSuite.slnx -c Release
$DOTNET tests/ToolSuite.LifecycleChecks/bin/Release/net10.0-windows/ToolSuite.LifecycleChecks.dll artifacts/lifecycle
$DOTNET modules/Pinto/tests/Pinto.ModuleChecks/bin/Release/net10.0-windows/Pinto.ModuleChecks.dll artifacts/pinto-module
$DOTNET modules/Pinto/tests/Pinto.DesktopChecks/bin/Release/net10.0-windows/Pinto.DesktopChecks.dll artifacts/pinto-desktop
```

## 合并状态

提交后执行 `git merge integration/tool-suite`：集成分支仍停在 `ecfbe76`（ticket 06 尚未并入，
`ticket/06-controls` 为独立分支），因此本次合并为 no-op。已知 06 的 `ToolSuite.slnx` 在
`/checks/` 同一行新增 `ToolSuite.SuiteChecks`，与本工单新增的 `ToolSuite.LifecycleChecks` 会形成
文本冲突；合并时应保留两个项目条目（语义合并）。06 未改动便签模块与本工单新增文件，
其 `SuiteController` 改动（失败重试前清理、主题变化去重与故障隔离）不影响本工单的模块生命周期。

## 行为说明

- 隐藏语义仍是“关闭 = 隐藏”：`Closing` 一律取消并排一次 `HideNote`，避免在关闭回调内再次 `Close()`
  造成重入；因此点关闭后窗口在同一个消息循环回合内完成保存与释放。
- 只有保存成功才释放窗口。失败时窗口、`NoteViewModel`、未提交编辑和待保存状态全部保留；
  修复磁盘后同一入口可继续隐藏，重建后编辑已持久化。
- 回收顺序为：移出 `_windows` → 关闭窗口（销毁 HWND）→ 摘 `HwndSource` 钩子 → 解绑 `PropertyChanged`
  → 清 `DataContext`/`ContextMenu`。隐藏便签在启动、重启和导入后都不建窗。
- 性能条隐藏时仅隐藏窗口（保留实例），但停止采样并暂停时钟；设置窗关闭即释放引用，重开重建。
- 剪贴板收起（`Dock`）清空共享缩略图缓存并释放已实现宿主的位图；缓存的代次规则使在途结果作废。
- 本次未修改统一设置窗口、托盘或主题宿主（工单 06 范围）；未修改剪贴板与性能条的生产代码，
  仅新增跨模块检查覆盖其既有生命周期。

## 未执行的人工/硬件场景

- **统一设置窗口的按需创建/关闭释放**：属工单 06，本分支未改动也未验证；合并后需以 06 的检查为准。
- **真实托盘菜单点击**“显示全部/隐藏全部/某张便签/性能条设置”：检查调用托盘使用的同一模块命令，
  未用鼠标点击真实托盘。
- **真实文件选择器**导入流程：导入对话框检查直接实例化真实窗口验证创建/关闭/重建；未驱动系统
  `OpenFileDialog`，也未验证取消选择后的完整导入链路。
- **真实多显示器与 DPI 切换**下的贴边恢复：检查使用当前会话工作区；未跨屏拔插或改缩放。
- **真实系统剪贴板截图与管理员窗口粘贴**：剪贴板检查使用假平台，不访问真实剪贴板。
- **整套 EXE 退出后的操作系统级句柄/进程观察**：检查在进程内验证“无模块窗口 + 数据文件可独占打开 +
  可重复启停”，未枚举进程句柄，也未在启用 GPU 指标时观察 `nvidia-smi` 查询子进程退出。
- **UAC/开机启动**：不在本工单范围，未执行。
- **内存/工作集读数**：按要求不测量、不报告节省数值。

## 风险与注意

- `Closing` 改为排队执行隐藏，点关闭与真正释放之间隔一个 Dispatcher 回合；这是避免 `Close()` 在
  `Closing` 内重入的必要处理。若有外部代码在该回合内断言窗口已消失，应等待消息循环。
- 隐藏现在会销毁 HWND 并重建视图模型。任何跨隐藏持有 `NoteWindow` 或原生句柄的调用方必须重新获取
  （本次同步修正了 `Pinto.ModuleChecks`、`LayoutChecks`、`DockingChecks`）；这是语义变化，
  不是兼容性回归。
- `WindowSummonOrderTests`（剪贴板顶层 z 序）在当前桌面会话稳定失败；已在基线 `ecfbe76`（暂存本工单
  改动后重建）复现同样 2 条失败，与本工单无关，需要真实交互桌面复核。
- 一次性运行 `dotnet test ToolSuite.slnx` 时出现过测试宿主并行导致的
  `SettingsWindow.ApplyTheme` 跨线程异常并中止；逐工程运行全部通过。该代码本工单未改动，
  属于测试并行/主题静态状态的既有约束。
- 剪贴板收起依赖 `PanelWindow.Dock()`；若将来新增不经过 `Dock` 的隐藏路径，需要复用同一清理入口。
- 性能条设置窗关闭后由宿主释放；模块保留的离线设置窗（工具停用时）也走关闭即释放，但未覆盖
  “离线设置窗打开期间停用/重启”的组合场景。
