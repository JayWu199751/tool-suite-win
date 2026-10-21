# 整合来源

2026-10-21 工单 01 读取源仓库版本及当前工作区状态；未修改源仓库，也未读取任何旧应用用户数据。

| 来源 | 提交 | 当时工作区 |
| --- | --- | --- |
| clipboard-wpf | ebcfdf093f7bed4eb7f391c131aedb9cdb06ce1f | 干净 |
| perf-monitor-wpf | af6541cd0f97b099e8307a80e60843990bd1c935 | 干净 |
| pinto-wpf | 248c051fcab11dd9acbda7bdae9cb574c8731000 | 23 项修改或新增，必须复制当前文件 |

便签已修改：README.md、四张 docs/screenshots 图片、src/Pinto/App.xaml、Services/NativeWindow.cs、Services/ThemeService.cs、Themes/Controls.xaml、Themes/Dark.xaml、Themes/Light.xaml、ViewModels/NoteViewModel.cs、Views/ImportDialog.xaml 与代码后置、Views/NoteWindow.xaml 与代码后置、tests/Pinto.DesktopChecks/CornerChecks.cs、Program.cs、TrayMenuChecks.cs。

便签已新增：docs/ui-design.md、src/Pinto/Themes/HighContrast.xaml、Themes/Tokens.xaml、tests/Pinto.DesktopChecks/VisualDesignChecks.cs。

后续接入各模块时记录复制清单及适配差异，不用 git archive 的最后提交替代便签当前文件。首票只创建独立宿主；不复制和启动三份旧 App。
