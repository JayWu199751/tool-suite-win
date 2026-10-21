# 视觉规范（Apple 参考实现）

本文件是 `docs/UI Design/Apple Light.md` 与 `Apple Dark.md` 在桌面应用中的落地映射。
所有可视窗口（剪贴板面板、性能条、性能设置、便签、导入对话框、套件设置、安装程序）
共用这一套 token；模块各自持有局部字典，但**取值必须与本文件一致**。

## 1. 设计原则

- **单一蓝色**：`#0071E3` 只用于主操作、开关打开态与焦点环；正文链接用 `#0066CC`（亮）/ `#2997FF`（暗）。
  不引入第二个彩色强调（网络箭头、阈值色一律改为中性层次）。
- **零投影**：层级只靠表面色差与圆角表达。上下文菜单、面板、卡片都不得使用 `DropShadowEffect`。
- **交替色带**：用 `#FFFFFF` 与 `#F5F5F7`（暗色 `#000000` / `#1D1D1F`）切分区域，不用分隔线。
  必须分隔时用 1px 发丝线 `#D6D6D6`（暗色 `#333336`）。
- **圆角**：容器/卡片 28，紧凑卡片 20，输入框与按钮为胶囊（高度的一半），链接焦点 10。
  交互元素不小于 10。
- **字重**：正文 400，标题 600，最大 600（不使用 700）。
- **留白优先**：内边距 16 / 20 / 28，行距紧凑标题（1.1）与放宽正文（1.45）。

## 2. 颜色

| Token | 亮 | 暗 | 用途 |
|---|---|---|---|
| `Surface.Canvas` | `#F5F5F7` | `#000000` | 窗口底层、内容带 |
| `Surface.Paper` | `#FFFFFF` | `#1D1D1F` | 卡片、工具条、输入井 |
| `Surface.Hover` | `#E8E8ED` | `#2A2A2C` | 悬停、开关关闭态轨道 |
| `Surface.Selected` | `#0F0071E3` | `#2E2997FF` | 选中行淡染 |
| `Ink.Primary` | `#1D1D1F` | `#F5F5F7` | 正文、读数 |
| `Ink.Secondary` | `#63636A` | `#86868B` | 次要文字、标签 |
| `Ink.Tertiary` | `#75757A` | `#A1A1A6` | 元信息、提示 |
| `Ink.Disabled` | `#A1A1A6` | `#55555C` | 不可用 |
| `Line.Hairline` | `#D6D6D6` | `#333336` | 发丝线、卡片描边 |
| `Line.Strong` | `#B8B8BD` | `#4A4A4E` | 输入井描边、窗口外环 |
| `Accent` | `#0071E3` | `#0071E3` | 主操作、开关开、焦点环 |
| `Accent.Link` | `#0066CC` | `#2997FF` | 文本链接、可点文字 |
| `State.Success` | `#1D8348` | `#30D158` | 成功提示 |
| `State.Danger` | `#C7261F` | `#FF453A` | 错误、破坏性操作 |

无障碍：`Ink.Secondary` 在 `Surface.Canvas`、`Surface.Paper`、`Surface.Hover` 上均 ≥ 4.5:1
（`#63636A` 与 `#E8E8ED` 为 4.88，`#86868B` 与 `#1D1D1F` 为 4.65），由
`Pinto.DesktopChecks/VisualDesignChecks` 与 `ToolSuite.UiTokenChecks` 守护。

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
| 剪贴板面板 `PanelWindow` | 亮 `#F5F5F7` / 暗 `#000000` | 亮 `#FFFFFF` / 暗 `#1D1D1F` | 工具条（搜索、页脚）用 Paper 形成上下色带；列表卡圆角 20 |
| 性能条 `PerformanceBarWindow` | 亮 `#FFFFFF` / 暗 `#000000`（跟随不透明度设置） | — | 无投影、1px 发丝外框、圆角 20；读数 Paper 色，标签 Secondary |
| 性能设置 `SettingsWindow` | `#F5F5F7` / `#000000` | `#FFFFFF` / `#1D1D1F`，圆角 28 | 标题 21/600，分组标题 12/600 |
| 便签 `NoteWindow` | `#FFFFFF` / `#1D1D1F` | — | 圆角 28，发丝描边，激活时描边 Strong |
| 导入 `ImportDialog` | 系统原生框架 | 选项卡 20 | 主选项为蓝色胶囊，其余幽灵按钮 |
| 套件设置 `SettingsWindow`（宿主） | `#F5F5F7` / `#000000` | `#FFFFFF` / `#1D1D1F` | 三工具行为卡片 + 主题胶囊 + 自启开关 |
| 安装程序 `InstallerForm` | `#F5F5F7` | `#FFFFFF`，圆角 20 | 主按钮蓝色胶囊，次按钮幽灵 |

## 5. 与参考稿的差异（有意为之）

1. **字阶整体下调**（17→14 正文）：参考稿是营销页，桌面常驻工具需要更高信息密度；
   层级关系与字重规则保持不变。
2. **紧凑卡片圆角 20**：列表行高度约 100 DIP，28 会在视觉上接近胶囊；
   大容器仍为 28。
3. **列表选中用 12% 蓝色淡染**而不是参考稿的整块填色：面板需要保持多行可读性。
4. **网络上下行不再用蓝/绿双色**：遵循"单一蓝色"的配比，方向用箭头字符区分。
5. **保留 1px 发丝描边**：Windows 桌面背景不可控，纯色差不足以在任意壁纸上界定窗口。

## 6. 实现约束

- 主题字典按模块分离（剪贴板 `Themes/{Light,Dark,HighContrast}.xaml`、
  便签 `Themes/`、性能与宿主各自的 `ThemeService`/`SuiteTheme`），不引入跨模块共享程序集。
- 字典键集合固定（剪贴板 19 个 `Brush.*`，便签 20 个角色名），三套主题键必须一致。
- 宿主把画刷与控件样式拆开：`SuiteTheme` 每次外观变化写入 `Suite*` 画刷键，
  控件模板固定在 `src/ToolSuite.App/Themes/HostControls.xaml`（`Suite.Card`、`Suite.SwitchRow`、
  `Suite.Combo`、`Suite.GhostButton`、`Suite.CloseButton`），只放键控样式，不写隐式样式，
  避免渗入模块窗口。
- 安装程序是独立的一次性对话框，只使用亮色一套：`tools/ToolSuite.Installer/InstallerUi.cs`
  提供圆角卡片（20）、胶囊按钮与 16 DIP 方形勾选的绘制原语，不参与宿主主题联动。
- 安装对话框的整个客户区由窗口自己一次画完（唯一子控件是安装目录输入框）。卡片、按钮、
  勾选若各自做成自绘子控件，窗口合成时多个表面会互相覆盖，悬浮重绘后留下成片重叠的文字，
  单面绘制是这条约束的原因，改回去会复发。
- 安装对话框的布局以 96 DPI 的 DIP 写死，再按 `DeviceDpi / 96` 统一换算（含圆角、间距、
  控件高度）。字号按点值随 DPI 放大，几何必须同比例，否则 150% 缩放下两者不同尺度、文字挤压。
- 静态事件订阅必须可退订（高对比度变化）。
