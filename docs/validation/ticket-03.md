# 工单 03 验证：完整剪贴板管理与图片能力

日期：2026-10-21。实现分支 `ticket/03-clipboard-full`，基于集成分支提交 `56a9206`。
所有检查使用工作树内 `artifacts/` 下的隔离数据根，不读取套件或原应用的用户数据，不改动
`C:/Users/10854/Code/clipboard-wpf`。检查通过注入的假剪贴板与假前台来源运行，未写入或读取真实系统剪贴板。

## 红绿证据

1. **图片/上限/搜索/键位/撤销/清空的领域检查先行**：扩展 `ToolSuite.ClipboardChecks`，覆盖 PNG 落盘与重启、
   同像素去重、文字与图片共用 200 上限（含置顶不裁剪）、多关键词搜索、四态键位矩阵、6 秒撤销与到期提交、
   清空联动删图片并取消待删除计时。首轮即通过——这些规则在从原项目复制时已完整，检查作为回归护栏保留。
2. **红 → 绿（真实缺口 1：托盘清空历史未接线）**：真实 WPF 检查通过模块公开命令入口执行“清空历史”，
   首轮失败：
   `System.Exception: clipboard module exposes no clear-history command`。
   在 `ClipboardModule.Commands` 增加 `clear-history`（运行期命令）并实现 `ClearHistory()`：
   `PendingDeletionService.ClearAll()`（先取消未到期的撤销计时，再清条目与 PNG）→ 重载列表 →
   toast「已清空历史」。重跑通过，图片目录清空、存档为 `[]`、重启后仍为空。
3. **红 → 绿（真实缺口 2：面板收起时仍解码不可见缩略图）**：真实面板检查先 `Dock()` 再注入新历史，
   首轮失败：
   `System.Exception: docked panel decodes thumbnails for invisible items`。
   原因是停靠后新增条目会让已实现容器重新请求缩略图，解码不可见内容。修复为面板级取图闸门：
   `PanelViewModel.ThumbnailsEnabled` 只在呼出时为 true，`ThumbnailHost.LoadEnabled`（默认 false）
   绑定该值，`Request()` 在闸门关闭时不发请求；呼出时绑定变化自动补取已实现容器。重跑通过，
   且重新呼出后可见项恢复加载（闸门不是永久关闭）。
4. **检查自身可失败**：上述两处红均为真实断言失败而非空转；期间用错的 F24 虚拟键（0x88）也直接表现为
   “呼出后仍停靠”的 `check condition timed out`，说明键位与联调用例不会静默通过。

## 新增/扩展的检查

`tests/ToolSuite.ClipboardChecks/Program.cs`（控制台，退出码非零即失败）：

1. PNG 记录到独立数据根 `images/<id>.png`、可解码；同像素重复复制命中并提升且不重写文件；
   重启后条目、路径、来源、备注可读；图片文件丢失的条目重启时被丢弃。
2. 文字与图片共用 200 条上限；199 条 + 1 条置顶 + 2 条新增时置顶条目不裁剪、超限先裁最旧普通条目；
   小上限下置顶图片保留、普通图片裁剪联动删除 PNG。
3. 正文/备注/来源（应用名、窗口标题、exe 路径）多关键词 AND 搜索、空查询回全量、高亮不丢字符。
4. 浏览态 8 个导航键 + 呼出键；搜索态只留 ↑↓/Enter/Esc；IME 组合、备注编辑、快捷键捕获各自让位；停靠只留呼出键。
5. 删除请求立即摘除但保留条目，`UndoWindowMs == 6000`；撤销恢复并取消计时；到期提交删除并清理 PNG；
   置顶/取消置顶（保留 pinnedAt）、备注归一化。
6. 清空历史：条目、图片文件、待删除计时全部清空，已取消的计时不再触发、不误报失败，重启为空。

`tests/ToolSuite.ClipboardUiChecks/Program.cs`（真实 WPF + 真实模块，三组场景）：

1. 文字链路回归：真实列表、全局快捷键呼出/收起、快捷键占用冲突、离线改键、保存失败保留窗口、停用不记录、重启恢复。
2. 图片与历史管理：真实模块记录图片（PNG 落盘、图卡文件名、来源信息）、呼出后缩略图实际加载、
   方向键边界、Z 置顶/取消、B 备注编辑并保存、Space 进搜索（正文+备注、来源应用名+窗口标题、无匹配空态）、
   搜索态 Esc 只退搜索、浏览态 Esc 停靠、Del 撤销窗口内保留 PNG、撤销恢复、Enter 将图片内容写入剪贴板
   并在无焦点快照时给出失败反馈（不向真实前台窗口注入按键）、删除 6 秒后真实到期落盘、清空历史命令。
3. 缩略图：只对已实现（可视）项解码、解码尺寸 = 150 DIP × 当前 DPI、文字卡不解码、换绑后每张卡片显示自己条目的像素
   （颜色校验，不串图）、解码失败不缓存且显示占位、条目消失按 id 差量失效、停靠清缓存并释放位图引用、
   停靠期间新增历史不解码、重新呼出恢复加载、停靠在途解码结果被作废不回填。

## 已运行命令与结果

SDK：`C:/Users/10854/AppData/Local/Microsoft/dotnet/dotnet.exe`。命令均在工作树根执行。

| 命令 | 结果 |
| --- | --- |
| `dotnet build ToolSuite.slnx -c Release` | 成功，0 警告，0 错误 |
| `dotnet test modules/Clipboard/ClipboardTool.Tests -c Release --no-build` | 481 通过 / 2 失败（均为 `WindowSummonOrderTests`，见风险） |
| `dotnet test modules/Clipboard/ClipboardTool.Tests -c Release --no-build --filter "Category!=Desktop"` | 481/481 通过 |
| `dotnet test modules/Clipboard/ClipboardTool.Tests -c Release --no-build --filter "FullyQualifiedName~WindowSummonOrderTests"` | 偶发：6 次中 1 次 2/2 通过，其余 0/2；基线代码同样（见风险） |
| `dotnet tests/ToolSuite.ClipboardChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardChecks.dll artifacts/verify-console` | 8 条 PASS，退出码 0 |
| `dotnet tests/ToolSuite.ClipboardUiChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardUiChecks.dll artifacts/verify-ui` | 4 条 PASS（含三组场景），退出码 0，约 12s |

可重跑命令（每次使用新的隔离根，已 gitignore）：

```bash
DOTNET=C:/Users/10854/AppData/Local/Microsoft/dotnet/dotnet.exe
cd C:/Users/10854/Code/tool-suite/.worktrees/03-clipboard-full
$DOTNET build ToolSuite.slnx -c Release
$DOTNET test modules/Clipboard/ClipboardTool.Tests -c Release --no-build
$DOTNET tests/ToolSuite.ClipboardChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardChecks.dll artifacts/verify-console
$DOTNET tests/ToolSuite.ClipboardUiChecks/bin/Release/net10.0-windows/ToolSuite.ClipboardUiChecks.dll artifacts/verify-ui
```

## 行为说明

- 清空历史与 legacy 一致**没有确认窗**：托盘命令立即执行，结果以 toast「已清空历史」反馈；
  面板收起时 toast 不可见，下次呼出可见，列表与图片目录的清空即时生效。
- 缩略图闸门默认关闭：`ThumbnailHost` 必须由面板显式置 `LoadEnabled=true` 才取图；
  模块内的绑定由 `PanelViewModel.ThumbnailsEnabled` 在呼出/停靠时切换。
- 图片只存 PNG（`images/<id>.png`），不保留原始剪贴板 DIB 字节；身份按规范化像素 SHA-1 判定，
  与原实现一致。

## 未执行的人工/硬件场景

- **真实系统剪贴板截图**：Win+Shift+S 截图后由监听自动入库、选中后粘贴回截图工具/画图。检查全部通过
  注入的假剪贴板，未触碰真实剪贴板，因此不声明该项已通过。
- **管理员窗口粘贴**：以管理员身份运行目标程序后粘贴图片/文字（需要 UAC 与真实前台窗口）。
- **真实多显示器/DPI 下的缩略图尺寸与停靠位置**：检查用当前会话 DPI 验证解码尺寸公式，未覆盖跨屏切换。
- **真实托盘菜单点击「清空历史」**：检查调用模块公开命令（托盘菜单的同一入口），未用系统鼠标点击托盘。
- **真实 6 秒撤销的人工操作**：到期提交由检查用真实 Dispatcher 计时（UI）与假调度器（领域）验证，
  未做人工撤销演示。
- **高对比度/系统主题切换**：检查只验证模块局部资源与宿主资源隔离，未做系统级主题切换的人工验证。

## 风险与注意

- `WindowSummonOrderTests`（Category=Desktop，顶层窗口 z 序）在当前桌面会话中偶发失败：本分支与
  基线 `56a9206`（暂存本工单改动后重建）表现一致，失败点为面板置顶后 `WindowFromPoint` 仍命中遮挡窗；
  6 次单独重跑有 1 次通过。该用例不涉及本工单行为，但需要真实交互桌面复核，未标记为通过。
- `clear-history` 为运行期命令（停用时不显示可用状态），未实现“停用状态下清空离线存档”，
  避免在未成功读档的会话里覆盖原件。
- 图片入库链路仍依赖 WIC 解码；无法解码的 DIB 在读取侧即被丢弃，损坏的 PNG 文件在列表显示占位且不缓存。
