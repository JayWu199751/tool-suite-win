# 视觉规范（Apple 参考实现）

本文件是 `docs/UI Design/Apple Light.md` 与 `Apple Dark.md` 在桌面应用中的落地映射。
统一设置复用性能条原窗口和控件，分组、对比度与紧凑布局见[统一设置页面](unified-settings.md)。
所有可视窗口（剪贴板面板、性能条、性能设置、便签、导入对话框、套件设置、安装程序）
共用这一套 token；模块各自持有局部字典，但**取值必须与本文件一致**。

## 1. 设计原则

- **单一色族**：亮色只用蓝 `#0071E3`（正文链接 `#0066CC`），暗色只用低饱和靛蓝 `#7B77E0`
  （动作文字与链接 `#A7A4F0`）；两套都只用于主操作、开关打开态与焦点环。
  不引入第二个彩色强调（网络箭头、阈值色一律改为中性层次）。
- **零投影**：层级只靠表面色差与圆角表达。上下文菜单、面板、卡片都不得使用 `DropShadowEffect`。
- **交替色带**：用 `#FFFFFF` 与 `#F5F5F7`（暗色 `#141419` / `#0B0B0E`）切分区域，不用分隔线。
  必须分隔时用 1px 发丝线 `#D6D6D6`（暗色 `rgba(255,255,255,0.06)`）。
- **圆角**：容器/卡片 28，紧凑卡片 20，输入框与按钮为胶囊（高度的一半），链接焦点 10。
  交互元素不小于 10。
- **字重**：正文 400，标题 600，最大 600（不使用 700）。
- **留白优先**：内边距 16 / 20 / 28，行距紧凑标题（1.1）与放宽正文（1.45）。

## 2. 颜色

暗色一列全部来自 `Apple Dark.md` 的 token；alpha 值原样保留，WPF 用 `#AARRGGBB` 书写：

| 参考稿 | 本项目写法 | 说明 |
|---|---|---|
| `rgba(255,255,255,0.06)` | `#0FFFFFFF` | 6% |
| `rgba(255,255,255,0.09)` | `#17FFFFFF` | 9% |
| `rgba(255,255,255,0.12)` | `#1FFFFFFF` | 12% |
| `rgba(123,119,224,0.13)` | `#217B77E0` | 13% |
| `rgba(123,119,224,0.45)` | `#737B77E0` | 45% |

| Token | 亮 | 暗 | 用途 |
|---|---|---|---|
| `Surface.Canvas` | `#F5F5F7` | `#0B0B0E` | 窗口底层、内容带 |
| `Surface.Paper` | `#FFFFFF` | `#141419` | 卡片、工具条 |
| `Surface.Input` | `#F5F5F7` | `#15151A` | 输入井、下拉井 |
| `Surface.Hover` | `#E8E8ED` | `#17FFFFFF` | 悬停、开关关闭态轨道 |
| `Surface.Selected` | `#0F0071E3` | `#217B77E0` | 选中行淡染、命中高亮 |
| `Ink.Primary` | `#1D1D1F` | `#F2F2F5` | 正文、读数 |
| `Ink.Secondary` | `#63636A` | `#A0A0AC` | 次要文字、标签 |
| `Ink.Tertiary` | `#75757A` | `#7E7E8A` | 元信息、提示 |
| `Ink.Disabled` | `#A1A1A6` | `#55555F` | 不可用 |
| `Line.Hairline` | `#D6D6D6` | `#0FFFFFFF` | 发丝线、卡片描边 |
| `Line.Strong` | `#B8B8BD` | `#1FFFFFFF` | 输入井描边、焦点外描边 |
| `Line.Selected` | `#0071E3` | `#737B77E0` | 选中卡片描边、焦点环 |
| `Window.Ring` | `#D6D6D6` | `#757575` | 无边框窗口的 1px 外环 |
| `Accent` | `#0071E3` | `#7B77E0` | 主操作、开关打开态、滑块已填充段 |
| `Accent.Link` | `#0066CC` | `#A7A4F0` | 文本链接、可点文字、勾选 |
| `Selection.Text` | `#1D1D1F` | `#FFFFFF` | 仅文本选区前景 |
| `State.Success` | `#1D8348` | `#57C08A` | 成功提示 |
| `State.Danger` | `#C7261F` | `#E05A52` | 错误、破坏性操作 |

无障碍：`Ink.Secondary` 在 `Surface.Canvas`、`Surface.Paper`、`Surface.Input`、`Surface.Hover` 上均 ≥ 4.5:1
（暗色 `#A0A0AC` 依次约 7.7 / 7.3 / 7.3 / 5.6，亮色 `#63636A` 与 `#E8E8ED` 为 4.88）。
暗色的悬停与描边是 alpha 淡染，对比度校验先按表面底色合成为不透明色再计算，
由 `Pinto.DesktopChecks/VisualDesignChecks` 守护。

高对比度：沿用系统色（`SystemColors.WindowColor/WindowTextColor/HighlightColor/...`），
不改写用户的对比度主题。

## 3. 字号与间距

桌面端在参考稿的展示级字阶上收一档，保持信息密度：

| 角色 | 字号 | 行高 | 字重 | 参考稿对应 |
|---|---|---|---|---|
| Display | 21 | 26 | 600 | display 80 |
| Title | 17 | 22 | 600 | heading 40 |
| Body | 14 | 20 | 400 | body 17 |
| Caption | 12 | 16 | 400 | caption 14 |
| Micro | 11 | 14 | 400/500 | micro 12 |

- 4px 基线：间距只用 4 / 8 / 12 / 16 / 20 / 24 / 28 / 32 / 40。
- 数字一律 `Typography.NumeralAlignment="Tabular"`（对应参考稿的 `"numr"`）。
- 字体：`Segoe UI Variable Display`（标题）/ `Segoe UI Variable Text`（正文），
  回退 `Segoe UI`、`Microsoft YaHei UI`；等宽用 `Cascadia Mono`。

## 4. 各窗口落地

| 窗口 | 底层 | 卡片 | 备注 |
|---|---|---|---|
| 剪贴板面板 `PanelWindow` | 亮 `#F5F5F7` / 暗 `#0B0B0E` | 亮 `#FFFFFF` / 暗 `#141419` | 工具条（搜索、页脚）用 Paper 形成上下色带；列表卡圆角 20 |
| 性能条 `PerformanceBarWindow` | 亮 `#FFFFFF` / 暗 `#0B0B0E`（跟随不透明度设置） | — | 无投影、1px 发丝外框、圆角 20；读数 Paper 色，标签 Secondary |
| 统一设置 `PerfMonitor.App.SettingsWindow` | `#F5F5F7` / `#0B0B0E` | `#FFFFFF` / `#141419`，圆角 28 | 宿主 `UnifiedSettingsPage` 在原性能条窗口内组合内容；通用区在顶部、三个工具分组独立折叠；标题 21/600，工具分组标题 15/600，内部分组标题 12/600 |
| 便签 `NoteWindow` | `#FFFFFF` / `#0C0C0E` | — | 圆角 28，发丝描边，激活时描边 Strong；比卡片再暗一档，菜单/提示仍用 `#141419` |
| 导入 `ImportDialog` | 系统原生框架 | 选项卡 20 | 主选项为蓝色胶囊，其余幽灵按钮 |
| 安装程序 `InstallerForm` | `#F5F5F7` | `#FFFFFF`，圆角 20 | 主按钮蓝色胶囊，次按钮幽灵 |

## 5. 与参考稿的差异（有意为之）

1. **字阶整体下调**（17→14 正文）：参考稿是营销页，桌面常驻工具需要更高信息密度；
   层级关系与字重规则保持不变。
2. **紧凑卡片圆角 20**：列表行高度约 100 DIP，28 会在视觉上接近胶囊；
   大容器仍为 28。
3. **列表选中用 13% 靛蓝淡染**（亮色 12% 蓝）而不是参考稿的整块填色：面板需要保持多行可读性。
4. **网络上下行不再用蓝/绿双色**：遵循"单一色族"的配比，方向用箭头字符区分。
5. **保留 1px 发丝描边**：Windows 桌面背景不可控，纯色差不足以在任意壁纸上界定窗口。
6. **暗色描边与悬停保留 alpha**（6% / 9% / 12% 白）：同一 token 要同时落在画布与卡片上，
   预乘成不透明灰只能在一种底色上正确；对比度校验按表面底色合成后再计算。
7. **开关打开态与滑块已填充段仍用实心 Accent**：参考稿"强调色永不实心填充"针对营销页的
   CTA；桌面二态控件的开/关必须一眼可辨，13% 淡染的轨道无法表达状态。除此之外不新增实心强调面。
8. **暗色窗口外环取 `#757575`**：这是参考稿中唯一跨明暗一致的外环色，无边框窗口浮在壁纸上时
   纯色差不足以界定边界；亮色外环仍为 `#D6D6D6`。
9. **暗色便签底色用 `#0C0C0E`**（比卡片 `#141419` 再暗一档）：便签直接压在任意壁纸上，
   与桌面之间没有别的表面过渡；浮在便签上的菜单、提示仍是 `#141419`，靠 1 DIP 发丝描边分层。
10. **完成圆圈与首行文字对齐**：圆圈不是钉在行顶，而是用一条同字体、同内边距的隐藏文本量出
   「首行高度」后在其中居中，因此换行与系统字号缩放下都对齐，多行项仍贴在首行。
11. **圆圈外缘对齐标题左缘**：圆圈只有 18 DIP，居中在 28 DIP 命中区里左右各留 5 DIP；
   若让命中区贴住行内边距，圆圈看起来就比标题多缩进一截。因此命中区向左借 4 DIP 行内边距，
   对齐的是圆圈的视觉外缘而不是命中区外缘；清单文字列与输入区不受影响。

## 6. 实现约束

- 主题字典按模块分离（剪贴板 `Themes/{Light,Dark,HighContrast}.xaml`、
  便签 `Themes/`、性能与宿主各自的 `ThemeService`/`SuiteTheme`），不引入跨模块共享程序集。
- 字典键集合固定（剪贴板 19 个 `Brush.*`，便签 20 个角色名），三套主题键必须一致。
- 取值来源分开：暗色一列以 `docs/UI Design/Apple Dark.md` 为准，亮色一列以 `Apple Light.md` 为准。
  两套共享同一组键，只改其中一套时不得顺手改另一套；安装程序固定用亮色一套，两者都不跟随。
- 暗色描边/悬停/选中是 alpha 画刷，`SolidColorBrush.Color` 不是最终观感；判断对比度或做像素比对时
  必须先按父表面合成为不透明色，否则会把 9% 白误当成纯白。
- 宿主把画刷与控件样式拆开：`SuiteTheme` 每次外观变化写入 `Suite*` 画刷键，
  控件模板固定在 `src/ToolSuite.App/Themes/HostControls.xaml`（`Suite.Card`、`Suite.SwitchRow`、
  `Suite.Combo`、`Suite.GhostButton`、`Suite.CloseButton`），只放键控样式，不写隐式样式，
  避免渗入模块窗口。
- 托盘右键菜单是 WPF `ContextMenu`（`NotifyIcon` 只负责弹菜单），皮肤集中在
  `src/ToolSuite.App/Themes/TrayMenu.xaml`，只合并进菜单自己的 `Resources`：Paper 浮层
  （`#141419` / `#FFFFFF`）、16 DIP 圆角、1 DIP 发丝描边、32 DIP 行高、10 DIP 圆角行、
  9% 白悬停（亮色 4% 黑）、零投影；分隔线用发丝线 + 上下 6 DIP。高对比度交还系统色。
- `ContextMenu` 是弹出层的根元素，**WPF 不会把它自己 `Resources` 里的隐式样式套到它自己身上**
  （与 `Window` 同理），`TrayMenu` 必须显式 `Style = Resources["Suite.TrayMenu"]`。漏掉这一步不会报错，
  而是整块退回系统默认模板：方角、灰描边、亮色分隔线，以及左侧压住首列文字的浅色滚动条衬底。
  同理，菜单里的 `Separator` 由 `MenuBase` 按 `MenuItem.SeparatorStyleKey` 取样式，
  隐式 `Separator` 样式对它无效，必须键控一份再让隐式样式 `BasedOn` 它。
  `ToolSuite.SuiteChecks` 的 `AssertTrayMenuSkin` 守着这两条。
- 应用图标固定为 `src/ToolSuite.App/Assets/app.ico`（纯白开口扳手、透明背景、图形占画布 95%，
  含 16–256 九档帧），exe 用它作 `ApplicationIcon`，窗口用 `Assets/app-icon-256.png`，
  托盘按 `SystemInformation.SmallIconSize` 取对应帧；资源缺失时才允许退回系统图标。
  不要再引入第二套图形或给图标加底色/描边。
- 安装程序是独立的一次性对话框，只使用亮色一套：`tools/ToolSuite.Installer/InstallerUi.cs`
  提供圆角卡片（20）、胶囊按钮与 16 DIP 方形勾选的绘制原语，不参与宿主主题联动。
- 安装对话框的整个客户区由窗口自己一次画完（唯一子控件是安装目录输入框）。卡片、按钮、
  勾选若各自做成自绘子控件，窗口合成时多个表面会互相覆盖，悬浮重绘后留下成片重叠的文字，
  单面绘制是这条约束的原因，改回去会复发。
- 安装对话框的布局以 96 DPI 的 DIP 写死，再按 `DeviceDpi / 96` 统一换算（含圆角、间距、
  控件高度）。字号按点值随 DPI 放大，几何必须同比例，否则 150% 缩放下两者不同尺度、文字挤压。
- 静态事件订阅必须可退订（高对比度变化）。
