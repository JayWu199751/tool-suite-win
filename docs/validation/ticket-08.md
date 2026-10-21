# 工单 08 验证：统一开机启动与自包含安装包

日期：2026-10-21。实现分支 `ticket/08-installer`，基于集成提交 `e107d2e`。
本轮为中断恢复后的补全：沿用未提交的自启适配、自包含安装器、构建/验证脚本与检查项目，
补齐部分写入回滚、真实便签可见性检查、真实安装包静默冒烟和本文档。
所有检查使用工作树 `artifacts/` 下的隔离根、可控 Run 存储与内存注册表，不读写真实开机启动项，
不安装到真实用户目录，不读取或修改原三个应用的数据与旧启动项。

## 红绿证据

检查不是“只打印 PASS”：每条断言失败都会抛异常并以非零退出码结束。为确认检查真的会失败，
临时移除实现后重跑，再恢复并重跑通过：

1. **红（部分写入未回滚）**：移除 `SuiteController.SetStartWithWindows` 失败分支中的
   `autoStart.Apply(previous)` 回滚后，`PartialWriteIsRolledBack` 失败：
   `System.Exception: 部分写入失败必须回滚注册表值`。
   **绿**：恢复回滚后通过；注册表写入已落盘但随后报错时，值被删除、设置不记为开启、
   界面保留真实失败原因。
2. **红（登录启动忽略启用选择）**：把 `StartAsync` 改成对存在模块一律启动后，
   `RunStartupSelectionAsync` 失败：`System.Exception: 登录启动不得重新启用停用的工具`。
   **绿**：恢复按 `Settings.EnabledTools` 过滤后通过；停用的性能条与便签不启动。
3. **红（升级不检查占用）**：令 `InstallerEngine.EnsureNotRunning` 直接返回后，
   `UpgradeRefusesWhileRunning` 在 `Throws<InstallerBlockedException>` 处失败。
   **绿**：恢复占用检查后通过；被占用的 EXE 拒绝升级、旧版本文件与卸载版本保持不变，
   解除占用后可重试升级。
4. **真实便签可见性**：新增检查用真实 `PintoModule` 和临时数据根启动，
   保存一张 `isVisible=false`、一张 `isVisible=true` 的便签；启动后只创建可见便签窗口，
   隐藏便签不建窗，停止后保存状态仍为 `"isVisible": false`。该检查在登录启动路径上直接验证
   “不强制显示隐藏便签”，不是用假模块推断。

## 已运行

- `C:/Users/10854/AppData/Local/Microsoft/dotnet/dotnet.exe build ToolSuite.slnx -c Release`
  → **0 警告、0 错误**。
- `./scripts/build-installer.ps1 -Dotnet "C:/Users/10854/AppData/Local/Microsoft/dotnet/dotnet.exe"`
  → 自包含发布与单文件安装包构建成功。产物：
  - 安装包：`artifacts/installer/ToolSuite-Setup-1.0.0.exe`
  - 大小：125,905,756 字节（120.1 MB）
  - SHA-256：`87acec34f8fbfde02eaf6fc6f8540cdbfc49b81b952e7f8b5f00c2334b63f8c8`
  - 发布目录：`artifacts/publish/win-x64`（174 MB，含 `coreclr.dll`、`hostfxr.dll`、
    `hostpolicy.dll`、`PresentationFramework.dll`、`PresentationCore.dll`、`WindowsBase.dll` 等）
  - 载荷：`artifacts/publish/ToolSuite-win-x64.zip`
  - `ToolSuite.runtimeconfig.json` 含
    `"includedFrameworks": [Microsoft.NETCore.App 10.0.13, Microsoft.WindowsDesktop.App 10.0.13]`，
    没有 `"framework"` 依赖项，说明发布产物不依赖目标机预装桌面运行时。
  - 构建信息：`artifacts/installer/build-info.txt`。
- `./scripts/verify-installer.ps1 -Dotnet ".../dotnet.exe" -Smoke`
  → 隔离检查 13 条全部 PASS，随后真实安装包冒烟通过：
  1. 开机启动默认关闭，首次启动不改动任何启动项
  2. 启用只写入 HKCU Run 的 `ToolSuite.Suite` 值并指向正式 EXE，原应用启动项不变
  3. 注册失败如实显示在设置界面且不记为生效，开关回到关闭
  4. 注册项已写入后报错时回滚，不留下实际生效但界面显示关闭的残留
  5. 注销只删除套件自己的启动值，失败时保留开启状态并报告原因
  6. 开机启动状态以套件自己的注册表值为准，外部变更不会伪装成已生效
  7. 登录启动只启动已启用工具，不重新启用停用工具
  8. 真实便签模块登录启动只显示保存为可见的便签，隐藏便签保持隐藏且状态不变
  9. 安装到套件自己的临时目录，写入卸载信息与可选快捷方式，不写启动项、不触碰原应用
  10. 套件文件被占用时拒绝升级并提示退出后重试，解除占用后可重试升级
  11. 卸载拒绝占用中的套件，成功后删除安装目录与套件注册项并保留用户数据与原应用数据
  12. 卸载入口自身运行时保留自身并清理其余安装内容与注册项
  13. 安装器命令参数支持 install/upgrade/uninstall/silent 与自定义目录
- 真实冒烟（同一脚本 `-Smoke`）：用最终安装包在
  `artifacts/installer-smoke/ToolSuite` 静默安装，再静默卸载。验证了：
  - 安装退出码 0，安装目录含 `ToolSuite.exe`、`coreclr.dll`、`PresentationFramework.dll`、
    `ToolSuite.Setup.exe`、`version.txt`；
  - HKCU 卸载登记 `DisplayName=工具套件`、`InstallLocation` 指向临时安装目录；
  - 安装没有写入 `ToolSuite.Suite` 开机启动值；
  - 卸载退出码 0，安装目录、套件卸载登记、临时冒烟根均被清理；
  - 未创建开始菜单/桌面快捷方式，未写开机启动项。
  - 冒烟前后真实状态检查：`UninstallKeyExists=False`、`SuiteRunExists=False`、
    `StartMenuLnkExists=False`、`SmokeDirExists=False`。
- 回归：
  - `ToolSuite.SuiteChecks` → 14 条工单 06 整合检查全部 PASS（真实 WPF/STA、真实三模块、隔离数据根）。
  - `./scripts/verify-host.ps1`（`DOTNET_ROOT` 指向 SDK）→ 4 组宿主检查 PASS，并生成
    `verification-passed.txt`；重复启动仍由原实例接收通知。

## 覆盖方式（实现要点）

- **自启身份**：`SuiteIdentity.RunValueName = ToolSuite.Suite`、`SuiteIdentity.UninstallKeyName`、
  安装目录与数据目录均为套件独立名字。生产适配器 `HkcuRunValueStore` 只按名字读写
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不枚举、不迁移、不删除原应用启动项。
- **默认关闭与状态来源**：首次启动从空注册表读取为关闭；控制器以注册表实际值为准覆盖
  `suite.json` 中的陈旧值，外部删除启动项后不会继续显示为已生效。
- **失败如实反馈**：注册、注销、写入后校验失败都会把开关恢复为操作前状态，写入
  `AutoStartError`、经统一错误入口上报并在设置窗口显示；部分写入会尽力回滚注册表值。
- **登录启动**：`SuiteController.StartAsync` 只对 `EnabledTools[id] == true` 且已注册的模块调用
  `SetEnabledAsync`；停用工具保持停用。便签模块只对 `IsVisible` 的便签建窗，隐藏便签保持无窗口。
- **安装/升级/卸载**：安装器先解压到同级 staging，确认载荷含 `ToolSuite.exe` 后整体替换；
  升级前对套件 EXE 与安装目录内的卸载入口做独占打开检查，占用时抛出
  `InstallerBlockedException`（可重试），旧目录先移到备份位，替换失败可恢复。卸载只删除
  安装目录、`Uninstall\ToolSuite`、`ToolSuite.Suite` 启动值和套件快捷方式，不引用
  `%LOCALAPPDATA%/ToolSuite`；从安装目录内卸载入口运行时保留自身，由独立 `cmd` 在进程退出后清理。
- **打包**：`PublishSingleFile` + `SelfContained`，载荷 zip 作为嵌入资源；安装器与套件发布均在
  仓库 `artifacts/` 下，脚本通过 `-Dotnet`/`DOTNET_ROOT` 解析 SDK，不依赖开发机绝对路径。
- **冒烟隔离**：临时安装目录固定在 `artifacts/installer-smoke`；开始前若发现已有
  `Uninstall\ToolSuite` 或 `ToolSuite.Suite` 则拒绝执行，避免覆盖真实登记或启动项。

## 未执行的人工场景

以下场景需要真实 UAC、登录会话或干净机器，未执行，**不标记为通过**：

- **真实 UAC 启动正式 EXE**：`ToolSuite.exe` 的 manifest 为 `requireAdministrator`；
  拒绝 UAC 时进程不会创建，因此不会回退为部分模块普通权限运行。本机检查通过
  `dotnet ToolSuite.dll` 运行，未点击真实 UAC 提权。
- **真实登录/重启后的开机启动**：未注销或重启 Windows；未实际确认登录时 HKCU Run 会启动
  套件并弹出 UAC。人工步骤：设置中开启开机启动 → 确认 Run 值指向安装后的
  `%LOCALAPPDATA%\Programs\ToolSuite\ToolSuite.exe` → 注销/重启 → 允许 UAC → 确认只有一个
  托盘、只出现已启用工具、隐藏便签仍隐藏。
- **真实已安装旧版本上覆盖升级**：升级占用拒绝与重试使用临时安装目录和文件锁验证，
  未在真实安装目录上运行旧版本进程后升级。
- **Windows“应用和功能”卸载**：已写入/读取/删除真实 HKCU 卸载登记，但未通过系统设置界面点击卸载。
- **干净 Windows 11 无 .NET 运行时启动**：以发布目录中的运行时文件和 `runtimeconfig.json`
  证明自包含，未在未安装 .NET 10 的干净机器上启动。
- **安装器交互界面、代码签名与 SmartScreen**：未人工点击安装向导、未签名安装包，
  未验证浏览器下载后的 SmartScreen 提示。
- **原三个应用的真实数据/启动项未被改动**：仅用可控 Run 存储和内存注册表模拟旧启动项，
  未读取或修改本机真实旧应用数据；也未删除任何真实旧启动项。

## 风险

- 正式套件要求管理员权限，登录自启会触发 UAC；用户拒绝时本次不会启动，这是规格要求，
  但会被部分用户理解为“自启失败”。需要人工在真实登录场景确认提示与结果。
- 安装包未签名，单文件自解压可能被杀毒软件延迟扫描或误报，首次启动/安装耗时不稳定。
- 安装目录同时包含约 120 MB 的卸载入口副本和展开后的自包含运行时，磁盘占用较高；
  卸载目录清理依赖进程退出后的 `cmd` 延迟删除，若被占用可能留下空目录。
- 升级占用检测依赖独占打开文件；极端共享模式或检查后到目录替换之间的竞态仍可能失败，
  失败时依赖备份目录回滚，但不会自动重试，需要用户退出后手动重试。
- 自启值保存绝对路径；用户手动移动安装目录后需要重新开关一次开机启动。
- 冒烟会临时写入真实 HKCU 卸载登记并在卸载时删除；若进程被强制中断，可能残留
  `HKCU\...\Uninstall\ToolSuite`，需要人工清理（脚本已在前置检查中发现已有登记时拒绝运行）。
- 开发目录中的正式 EXE 也可管理开机启动（只排除 `dotnet` 宿主）；开发时误开机会把开发路径
  写入 Run 值，需在发布前确认用户使用的是安装后的 EXE。
