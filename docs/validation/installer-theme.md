# 安装器跟随系统亮暗主题

2026-10-23。

## 原因与修复

安装器原先只有固定浅色调色板，既不读取系统应用主题，也不处理主题变化通知。
安装目录输入框另有独立颜色，原生标题栏也没有设置深色外观。

安装窗口现在读取 Windows 的 `AppsUseLightTheme`，在启动及
`WM_SETTINGCHANGE`、`WM_THEMECHANGED`、`WM_SYSCOLORCHANGE` 后刷新实例调色板、
输入框、自绘控件和原生标题栏。高对比度优先使用系统颜色。
错误状态保存为状态标记，避免切换主题后仍使用旧主题的错误文字颜色。

## 回归检查

```powershell
dotnet run --project tests/ToolSuite.InstallerUiChecks -c Release -- artifacts/installer-theme-check
```

检查项目直接编译真实安装器源文件，不依赖嵌入载荷，不调用安装或卸载入口。
通过注入主题读取结果并向真实窗口发送系统通知，验证：

- 浅色和深色启动时，实际客户区绘制及目录输入框颜色正确。
- 三类系统通知后能双向切换，标题栏颜色设置同步。
- 切换主题保留已输入的安装目录。
- 默认构造路径读取当前实际系统偏好，高对比度调色板使用系统颜色。

修复前连续两次稳定失败：`FAIL 系统主题变化预期深色画布，实际 RGB(245,245,247)`。
修复后通过，输出浅色和深色客户区预览。
检查已接入 `scripts/verify-installer.ps1`。

既有 `ToolSuite.InstallerChecks` 隔离检查全部通过，正式安装器构建为 0 警告、0 错误。
已通过 `scripts/build-installer.ps1` 重新生成 `artifacts/installer/ToolSuite-Setup-1.0.0.exe`。

验证没有修改 Windows 主题设置，也没有执行真实安装或卸载。
主题切换通知由测试发送；未通过 Windows 设置页面切换系统主题。
