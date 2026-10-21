# 统一设置验证记录

日期：2026-10-22。需求见[统一设置页面](../unified-settings.md)。
所有检查使用仓库 `artifacts/` 内的隔离数据，不读写日常套件数据或真实开机启动项。

## 实现与行为

- `SettingsHost` 维护唯一设置窗口；`UnifiedSettingsPage` 在实际的 `PerfMonitor.App.SettingsWindow` 中组合通用与工具分组。
- 保留性能条原 XAML 控件、配置验证、错误反馈与按显示器 DPI 限制高度的逻辑。
- 主题/自启仍由套件控制器持有；剪贴板快捷键和性能条配置仍调用各模块已有保存事务。
- 新增三个分类的展开偏好，保存在既有 `suite.json` 中；旧配置不含该字段时默认全部收起。
- 模块设置请求交给宿主定位分组，性能条右键菜单也经过相同入口；打开设置不启动已停用模块。
- 便签保留原有备份和恢复操作；未新增默认颜色、默认贴边等配置。

## 自动检查

| 检查 | 结果 |
| --- | --- |
| `dotnet build ToolSuite.slnx -c Release` | 通过，0 警告、0 错误 |
| `ToolSuite.SuiteChecks` 原有整合检查 | 通过 |
| `UnifiedSettingsChecks` | 8 组通过，覆盖真实托盘、模块入口、性能条实际右键菜单、独立折叠、重建恢复、离线编辑、运行编辑、错误回滚、损坏恢复提示、亮暗与键盘焦点 |
| `ClipboardTool.Tests` | 483/483 通过 |
| `PerfMonitor.Core.Tests` | 162/162 通过，含菜单与设置直接启用居中的参数化检查 |
| `PerfMonitor.Windows.Tests` | 最终复验 50/50 通过 |
| `PerfMonitor.App.Tests` | 24/24 通过 |
| `Pinto.Checks`、`Pinto.ModuleChecks`、`Pinto.DesktopChecks` | 全部通过 |
| `ToolSuite.HostChecks`、`ClipboardChecks`、`ClipboardUiChecks` | 全部通过 |
| `ToolSuite.LifecycleChecks`、`ToolSuite.InstallerChecks` | 全部通过，生命周期在最终修复后再次通过 |
| `scripts/verify-host.ps1` | 通过：真实宿主设置打开/关闭、窗口释放、重复实例通知和退出 |

新增检查复验命令（每次选一个新的隔离数据目录）：

```powershell
dotnet run --project tests/ToolSuite.SuiteChecks -c Release -- artifacts/unified-settings-recheck --unified-settings
```

测试过程中的失败与处理：

1. 新界面检查先在旧实现上失败，随后统一入口与三个分组通过。
2. 审查发现运行时补丁误携带模块自启请求；新增运行时字号保存检查先失败，修复后同时验证落盘和实际窗口字号更新。
3. 审查发现设置页开启居中未立即调整位置；扩展既有位置检查覆盖菜单/直接设置两个入口，先失败后修复。
4. 补充检查确认保留损坏配置恢复提示；原有提示在合并时遗漏，先失败后修复。
5. 首轮 Windows 检查有一项真实任务栏 HWND 层级用例失败；该用例单独复验通过，随后整套 Windows 检查 50/50 通过。
   首轮日志保留于 `artifacts/unified-settings-regression/`，未删除失败证据。

## 设计检查

评价：**Good**。页面以“通用始终可见、工具按需展开”为中心，沿用性能条现有控件和卡片语言。

- 亮暗与 420/480 DIP 预览已渲染并人工查看，文件在 `artifacts/unified-settings-complete/unified-settings/`。
- 正文/次要文字在卡片上的对比度分别为亮色 16.83/5.96、暗色 16.43/7.10，超过 4.5:1。
- 分组使用 WPF `Expander` 与标准展开/收起辅助功能接口；有名称、48 DIP 标题点击区域与键盘焦点样式。
- 性能条开关保留 40×24 DIP 视觉轨道，点击区域扩大至 48×32 DIP；设置内容仅有一层纵向滚动。
- 不增加折叠动画、玻璃或阴影；高对比度代码使用系统语义画刷。
- 指引依据：`layout.md › Visual hierarchy`、`settings.md › Best practices`、`scroll-views.md › Best practices`、
  `accessibility.md › Vision/Mobility`、`typography.md › Ensuring legibility`。具体配色和 Windows 托盘入口遵循项目既有约定。

未人工切换系统高对比度或运行屏幕阅读器，未执行真实多显示器移动、系统 200% 文本与 UAC 场景；不将这些场景记录为已通过。

## 两轴审查

基线为本轮开始前的工作区快照，排除之前未提交的界面改动与文件整理。

- Standards：2 项低优先级发现（4px 间距与窗口映射文档）均已修复，复核无剩余发现。
- Spec：2 项行为发现（运行设置误触发自启校验、居中不立即生效）均已修复，并以失败→通过检查确认；复核无剩余发现。

## 本轮维护记录

现有完整设置页属于性能条，剪贴板仅有快捷键小窗。此次将实际窗口归属、入口和验证命令写入需求及交付文档，
并把统一页检查接入已有 `ToolSuite.SuiteChecks`，后续运行原检查入口即可覆盖合并页面。

## 窗口尺寸修正

统一设置保留性能条原设置页的固定 420 DIP 宽度，禁止拖动改变尺寸；高度固定为 720 DIP，小屏按工作区缩小。
展开或收起分类仅改变内部滚动内容，不改变窗口尺寸。

- 真实 WPF 检查先复现高度随内容变化，以及宽度偏离原有 420 DIP；修复后通过。
- 全部分类展开/收起时高度保持不变，长内容出现内部滚动条；亮暗主题和固定 420 DIP 宽度检查通过。
- 独立输出目录 Release 构建通过（0 警告、0 错误），统一设置整合检查全部通过。
- 本次预览与隔离数据：`artifacts/settings-size-final/unified-settings/`。
## 滚动条布局修正

默认 ScrollViewer 模板在滚动条出现时额外占用约 17 DIP，导致内容从 380 DIP 缩至约 363 DIP。
改为设置页专用模板，将 10 DIP 滚动条放在已有右侧留白，保持窗口与内容宽度不变。

- 真实 WPF 回归先失败后通过：全部展开和收起后窗口为 420 DIP，视口与内容宽度保持不变。
- 验证滚动条不遮挡内容，并通过真实 Thumb 拖动事件确认仍能滚动。
- Release 构建 0 警告、0 错误，统一设置整合检查通过。
- 隔离数据和预览：`artifacts/settings-scroll-final-pass/unified-settings/`。
## 恢复早期窗口宽度

继续核对 Git 历史后确认：`0158fb5` 视觉改版前，性能条设置页为 380 DIP；该提交将其改为 420 DIP。
前次尺寸修正误以最近版本的 420 DIP 为用户记忆中的原宽度，现将统一页恢复为 380 DIP，高度仍为 720 DIP。
独立性能条窗口保留其既有尺寸。

- Release 构建 0 警告、0 错误，统一设置整合检查通过。
- 380 DIP 下滚动条显示/隐藏均不改变窗口或内容宽度，拖动滚动和全部设置入口检查通过。
- 亮暗主题预览：`artifacts/settings-width-380/unified-settings/`。