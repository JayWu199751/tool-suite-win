# 登录自启与管理员权限兼容性修复

2026-10-23。

## 复现与原因

真实 `HKCU\...\Run\ToolSuite.Suite` 存在且被系统标记为启用，目标 EXE 文件存在。
在普通权限进程中，通过 `ProcessStartInfo.UseShellExecute=false` 启动同一 EXE，稳定返回
Win32 740（请求的操作需要提升），进程未创建。宿主清单为 `requireAdministrator`；
旧实现只确认 Run 值存在，因此错误地把“写入成功”视为“登录后能够运行”。

## 修复

- 使用 Windows 任务计划程序：当前用户登录触发、InteractiveToken、HighestAvailable。
- 不保存密码，不使用服务账户；取消电池限制和默认执行时限，忽略重复实例。
- 原半失败 Run 自启在提权宿主首次启动时迁移。任务注册成功才删除旧项，失败回滚并保留原项。
- 有效状态检查用户身份、触发器、权限、启用状态、动作和当前 EXE；错误路径不能显示为生效。
- 关闭与卸载清理套件自己的任务。卸载若无权清理任务，保留程序文件以便提权后重试。

任务权限设计依据：[微软任务安全上下文文档](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks)。

## 检查命令与边界

```powershell
dotnet run --project tests/ToolSuite.InstallerChecks -c Release -- artifacts/autostart-regression --autostart-only
```

回归检查覆盖旧值迁移、创建失败、旧值删除失败回滚、关闭、错误目标与状态回读。
使用 Windows 自己的任务定义解析器验证真实注册 XML，包括最高权限、当前用户交互会话、
登录触发、含空格路径、电池限制与时限。无需创建测试自启项。
既有完整隔离检查还覆盖安装、升级、卸载和模块/窗口状态恢复。
完整隔离检查已通过，包含卸载时任务删除失败保留文件、成功后删除任务的回归断言。
安装器亮暗主题检查也通过，已重新生成 `artifacts/installer/ToolSuite-Setup-1.0.0.exe`。

当前代理进程未提权，未修改真实自启项，也未注册或执行真实最高权限任务；未注销或重启系统。
安装更新后，需要手动启动一次并允许 UAC，才能完成已有自启项迁移；真实登录验证仍需在此之后进行。

## 首次启用报 0x80070002 的补充修复

安装后尚无登录任务时，真实 `Schedule.Service` 的 `GetTask` 返回 `0x80070002`。
.NET 动态 COM 调用将其映射为 `System.IO.FileNotFoundException`，原实现仅捕获
`COMException`，导致启用流程在读取旧状态时提前失败，根本没有执行任务注册。
这不是目标 EXE 缺失，也不是任务 XML 或注册权限错误。

读取和删除任务现在同时识别这两种异常类型，且严格限定 HRESULT 为 `0x80070002`。
读取不存在任务返回未开启，删除不存在任务视为成功；权限等其他错误继续上报。

新增检查通过内部任务名构造入口，以唯一隔离名称直接访问真实 Windows COM 接口，
验证读取、状态查询、关闭和重复删除。测试不注册任务、不修改用户的真实自启项。
修复前上述命令稳定以 `FileNotFoundException` 失败，修复后通过。
真实最高权限任务注册与重启验证仍受前述未提权环境限制。
