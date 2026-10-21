using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ClipboardTool.Application;
using ClipboardTool.Domain.History;
using ClipboardTool.Domain.Hotkeys;
using ClipboardTool.Domain.PanelModes;
using ClipboardTool.Domain.PasteChain;
using ClipboardTool.Domain.Settings;
using ClipboardTool.Infrastructure.Windows;

namespace ClipboardTool.Presentation.Wpf;

// 基类全限定：Application 层命名空间会遮蔽 System.Windows.Application
public sealed class ClipboardModule : ToolSuite.Core.IToolModule
{
    private readonly ClipboardPlatform? _platform;
    public ClipboardModule(ClipboardPlatform? platform = null) => _platform = platform;
    private HotkeyExecutor? _hotkeys;
    private PanelWindow? _panel;
    private HistoryService? _history;
    private ClipboardWatchService? _watch;
    private PasteService? _paste;
    private ModeExecutor? _executor;
    private IClipboardChangeEventSource? _clipboardSource;
    private PanelCoordinator? _coordinator;
    private PendingDeletionService? _deletion;
    private MouseHook? _mouseHook;
    private JsonStore? _settingsStore;
    private AppSettings _settings = AppSettings.Default;
    private DispatcherTimer? _captureSuccessTimer;
    private ToolSuite.Core.ModuleContext? _context;
    private string _dataDir = string.Empty;
    private DiagnosticLog _diagnostics = DiagnosticLog.None;
    private SummonKeyRetry? _retry;
    private long _generation;
    private bool _stopping;
    private Dispatcher Dispatcher => System.Windows.Application.Current.Dispatcher;
    public string Id => "clipboard";
    public string DisplayName => "剪贴板";
    public IReadOnlyList<ToolSuite.Core.ToolCommand> Commands =>
        [
            new("change-shortcut", "修改剪贴板快捷键", _ => { OpenShortcutSettings(); return Task.CompletedTask; }, false),
            // 清空历史（规格「剪贴板分组包含…清空历史」）：与 legacy 一致立即执行、无确认窗，
            // 完成经 toast 反馈；条目与 PNG 联动、待删除计时一并取消（次序收口在 PendingDeletionService.ClearAll）。
            new("clear-history", "清空历史", _ => { ClearHistory(); return Task.CompletedTask; }),
        ];

    /// <summary>清空全部历史（托盘命令）。运行期入口：先取消未到期的删除撤销窗口，再清条目与图片文件，
    /// 列表随历史变更事件重载（缩略图缓存按消失 id 差量失效），最后给出结果反馈。</summary>
    public void ClearHistory()
    {
        if (_deletion is null || _history is null) throw new InvalidOperationException("剪贴板未运行。");
        _deletion.ClearAll();
        _panel?.ReloadEntries(_history.Entries, _deletion.HiddenIds);
        _panel?.ShowToast("已清空历史", null, isError: false, actionLabel: null, onAction: null);
    }

    /// <summary>焦点快照的真源在协调器（FocusTargetSnapshot）：呼出时补拍、退出输入态复用、
    /// 隐藏面板时消费。粘贴链路经端口读取同一份，不再有第二份存储。</summary>
    private void Dispatch(Action action)
    {
        var generation = _generation;
        Dispatcher.BeginInvoke(() => { if (!_stopping && generation == _generation) action(); });
    }

    private FocusTarget? CapturedFocus => _coordinator?.FocusTargetSnapshot;

    public void Configure(ToolSuite.Core.ModuleContext context)
    {
        _context = context;
        _dataDir = context.DataDirectory;
    }

    private Window? _shortcutWindow;
    private void OpenShortcutSettings()
    {
        if (_shortcutWindow is not null) { _shortcutWindow.Activate(); return; }
        if (_context is null) throw new InvalidOperationException("Module context is missing.");
        var store = _settingsStore ?? new JsonStore(_dataDir);
        var settings = _panel is null ? store.ReadSettings() : _settings;
        var input = new System.Windows.Controls.TextBox { Text = settings.Shortcut, Margin = new Thickness(0,8,0,12) };
        var feedback = new System.Windows.Controls.TextBlock { TextWrapping = TextWrapping.Wrap };
        var save = new System.Windows.Controls.Button { Content = "保存", Padding = new Thickness(18,6,18,6) };
        var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "呼出快捷键（例如 Control+Shift+V）" });
        stack.Children.Add(input); stack.Children.Add(save); stack.Children.Add(feedback);
        var window = new Window { Title = "剪贴板快捷键", Width = 410, Height = 210, Content = stack, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        save.Click += (_,_) => { try { SetShortcut(input.Text); window.Close(); } catch(Exception ex) { feedback.Text=ex.Message; } };
        window.Closed += (_,_) => _shortcutWindow = null;
        _shortcutWindow = window;
        window.Show();
    }

    public void SetShortcut(string shortcut)
    {
        if (_context is null) throw new InvalidOperationException("Module context is missing.");
        var combo = AccelCodec.Parse(shortcut) ?? throw new ArgumentException("快捷键格式无效。");
        var verdict = CaptureKeyRules.Validate(combo);
        if (verdict != CaptureVerdict.Ok) throw new ArgumentException(CaptureKeyRules.MessageOf(verdict));
        var store = _settingsStore ?? new JsonStore(_dataDir);
        var current = _panel is null ? store.ReadSettings() : _settings;
        var previous = AccelCodec.Parse(current.Shortcut) ?? HotkeyPlan.SummonDefault;
        if (_panel is null || combo != previous)
        {
            if (!HotkeyAvailability.CanRegister(combo))
                throw new InvalidOperationException($"{combo.DisplayName} 已被占用或无效，请换一个。");

        }
        var next = current with { Shortcut = AccelCodec.Encode(combo) };
        if (_panel is not null)
        {
            _coordinator!.SetToggleShortcut(combo);
            if (!_hotkeys!.EffectiveKeys.Contains(combo))
            {
                _coordinator.SetToggleShortcut(previous);
                throw new InvalidOperationException("快捷键注册失败，保留原快捷键。");
            }
        }
        try { store.SaveSettings(next); }
        catch { _coordinator?.SetToggleShortcut(previous); throw; }
        _settings = next;
    }

    public Task StartAsync(ToolSuite.Core.ModuleContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Configure(context);
        _generation++;
        _stopping = false;
        _diagnostics = new DiagnosticLog(new FileDiagnosticSink(_dataDir), null, Environment.ProcessId);
        var dataDir = _dataDir;
        _settingsStore = new JsonStore(dataDir);
        _settings = _settingsStore.ReadSettings();
        _panel = new PanelWindow(_diagnostics);
        _hotkeys = new HotkeyExecutor();
        _executor = new ModeExecutor(_diagnostics);
        _coordinator = new PanelCoordinator(new PanelModesHost(this), new DispatcherDelayScheduler(Dispatcher));

        ApplyTheme(context.Theme);
        // 共享缩略图缓存（T06）：解码 Task.Run 后台线程（结果 Freeze）、回调归队 UI；
        // 双上限（32 张 / 24 MiB）、按 Id 记忆化、失效与代次规则在缓存内部
        _panel.SetThumbnailCache(new ThumbnailCache(
            WpfThumbnailDecoder.Decode,
            work => Task.Run(work),
            action => Dispatch(action)));

        // 存档目录由套件上下文注入，不读取旧应用数据。
        var store = new HistoryStore(
            HistoryStore.DefaultMaxHistory,
            new ImageFileStore(dataDir),
            // 新 id UUID 带连字符形状（与 legacy uuid::Uuid::new_v4、旧档样例一致，02-spec/02 §1）
            () => Guid.NewGuid().ToString(),
            () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _history = new HistoryService(store, _settingsStore);
        _history.LoadFromStorage();
        _watch = new ClipboardWatchService(
            _platform?.Reader ?? new ClipboardReader(), _platform?.Sequence ?? new ClipboardSequenceReader(), _history, _platform?.Writer ?? new ClipboardWriter(),
            // 来源应用采集（F06）：复制处理时取前台窗口信息，失败降级未知来源
            _platform?.Foreground ?? new ForegroundSource(), _diagnostics);
        _paste = new PasteService(
            _history,
            _watch,
            capturedFocus: () => CapturedFocus,
            restoreAndPaste: target => FocusPasteRestore.RestoreAndPaste(target, paste: true),
            hidePanel: HidePanelAfterPaste,
            reportFocusError: (stage, _) => ShowStatus(PasteChain.FocusErrorMessage(stage)));

        // 延迟删除（F25）：摘除/撤销窗口/到期真删。计时经 Dispatcher 调度器在 UI 线程触发，
        // 与面板编排同线程封闭；跨停靠/呼出继续计时（秒表语义，不随面板隐藏重置）。
        _deletion = new PendingDeletionService(new DispatcherDelayScheduler(Dispatcher), _history);
        _deletion.HiddenChanged += () => Dispatch(() =>
            _panel?.ReloadEntries(_history!.Entries, _deletion!.HiddenIds));
        _deletion.ToastRequested += toast => Dispatch(() =>
            _panel?.ShowToast(toast.Message, toast.Dim, toast.IsError, toast.ActionLabel, toast.OnAction));

        // 历史变更（监听线程/链路线程触发）归队 UI 渲染；遮罩随快照重注（到期收尾/撤销都会通知）
        _history.EntriesChanged += () => Dispatch(() =>
            _panel?.ReloadEntries(_history!.Entries, _deletion!.HiddenIds));
        _panel.ReloadEntries(_history.Entries, _deletion.HiddenIds);

        // —— 面板事件 → 意图/编排（UI 线程封闭；协调器状态与全部效果调用都在该线程） ——
        _panel.CardPasteRequested += RequestPaste;
        _panel.SearchActivationRequested += EnterSearch;
        _panel.ThemeToggleRequested += () =>
        {
            var current = _context?.ReadThemePreference?.Invoke() ?? ToolSuite.Core.ThemePreference.System;
            _context?.RequestThemeChange?.Invoke(current switch {
                ToolSuite.Core.ThemePreference.System => ToolSuite.Core.ThemePreference.Light,
                ToolSuite.Core.ThemePreference.Light => ToolSuite.Core.ThemePreference.Dark,
                _ => ToolSuite.Core.ThemePreference.System });
        }; // F26 面板入口：三态循环走权威 Toggle（失败保留原偏好可重试）
        _panel.CompositionChanged += composing => _coordinator.SetComposing(composing);
        _panel.NoteEditExitRequested += () => _coordinator.ExitInput(PanelMode.NoteEdit, restoreFocus: true);
        _panel.PinRequested += id => _history.TogglePin(id); // 取消置顶保留 pinnedAt（存档契约）
        // 删除先放弃指向该条的编辑（legacy hide 分支清 noteEdit；无编辑则退态/放弃均无操作）
        _panel.DeleteRequested += id =>
        {
            _panel.AbandonNoteEditIfEditing(id);
            _coordinator!.ExitInput(PanelMode.NoteEdit, restoreFocus: false);
            _deletion!.Request(id);
        };
        _panel.NoteSaveRequested += SaveNote;
        _panel.CaptureAttempted += OnCaptureAttempted;
        _panel.CaptureCancelled += OnCaptureCancelled;

        _clipboardSource = _platform?.CreateSource() ?? new ClipboardMessageSource();
        _watch.Start(_clipboardSource);

        // —— 单击外部停靠（F17）：全局低级鼠标钩子上报真实按下（回调只记录，ADR-0004），
        //    判定（可见/时间窗/面板外）在 Domain.ExternalClickRules，命中走停靠编排。
        //    事件在钩子转发线程触发，归队 UI 线程后与协调器状态同线程封闭判定。
        _mouseHook = new MouseHook();
        _mouseHook.Pressed += (x, y, atMs) =>
            Dispatch(() => DockIfClickedOutside(x, y, atMs));
        _mouseHook.Start();

        // 键位计划由协调器按四态推导差量（F18–F21）：停靠态={呼出键}；呼出浏览态=呼出键+八导航键；
        // 搜索/备注/捕获按让位矩阵增删。注册动作在宿主 RegisterKey 里按动作分发（呼出/导航）。
        // 注意顺序：面板自身在 SourceInitialized 里先 Dock，此前协调器尚未给键；初始计划由
        // Attach 之后的 SetToggleShortcut 落地（停靠态目标集合）。呼出键来自设置（码表解码）。
        _panel.SourceInitialized += (_, _) =>
        {
            _hotkeys.Attach(_panel);
            _coordinator.SetToggleShortcut(ParsedToggle());
            _diagnostics.Vital($"window-ready ui=wpf hwnd=0x{_panel.Hwnd.ToInt64():X}");
        };
        _panel.ContentRendered += (_, _) => _diagnostics.Vital("wpf-ui-ready");

        // 呼出键没注册上=整会话热键哑；按 5s/20s 现读设置重试两遍（差量幂等，F32）
        _retry = new SummonKeyRetry(
            new DispatcherDelayScheduler(Dispatcher),
            readCurrent: () => ParsedToggle(),
            apply: combo => _coordinator!.SetToggleShortcut(combo));
        _retry.Start();

        _panel.Show();
        var readable = WindowPlacer.TryGetPhysicalRect(_panel.Hwnd, out var warmupRect);
        _diagnostics.Vital($"warmup ui=wpf docked={_panel.IsDocked} readable={readable} " +
            $"actual={warmupRect.Left},{warmupRect.Top},{warmupRect.Right},{warmupRect.Bottom}");


        if (!_hotkeys.EffectiveKeys.Contains(ParsedToggle()))
            _context!.ReportError($"{ParsedToggle().DisplayName} 已被占用，请在统一设置修改快捷键。");
        return Task.CompletedTask;
    }

    public async Task PrepareToStopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_panel is null) return;
        _panel.CommitPendingEdit();
        await (_executor?.DrainAsync() ?? Task.CompletedTask);
        await (_watch?.SaveCurrentAsync() ?? Task.CompletedTask);
        _settingsStore!.SaveSettings(_settings);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        _generation++;
        _retry?.Dispose(); _retry = null;
        _captureSuccessTimer?.Stop(); _captureSuccessTimer = null;
        if (_coordinator is not null) _coordinator.Hide(restoreFocus: false);
        _hotkeys?.Dispose(); _hotkeys = null;
        _deletion?.Dispose(); _deletion = null;
        if (_mouseHook is not null) await _mouseHook.StopAsync();
        _mouseHook = null;
        _watch?.Dispose();
        if (_clipboardSource is ClipboardMessageSource nativeSource) await nativeSource.StopAsync();
        else if (_clipboardSource is IDisposable disposableSource) disposableSource.Dispose();
        _clipboardSource = null;
        if (_executor is not null) await _executor.StopAsync();
        _executor = null;
        _shortcutWindow?.Close(); _shortcutWindow = null;
        _panel?.ReleaseResources(); _panel?.Close(); _panel = null;
        _watch = null; _history = null; _paste = null; _coordinator = null;
    }

    public void ApplyTheme(ToolSuite.Core.ThemePreference theme)
    {
        if (_panel is null) return;
        var dark = theme == ToolSuite.Core.ThemePreference.Dark;
        _panel.Resources.MergedDictionaries.Clear();
        _panel.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri(dark ? "/ClipboardTool;component/Themes/Dark.xaml" : "/ClipboardTool;component/Themes/Light.xaml", UriKind.Relative) });
        _panel.Resources["Font.Sans"] = new System.Windows.Media.FontFamily("Microsoft YaHei, Segoe UI");
        _panel.Resources["Font.Mono"] = new System.Windows.Media.FontFamily("Cascadia Code, Consolas");
        _panel.SetThemePreference((_context?.ReadThemePreference?.Invoke() ?? theme) switch { ToolSuite.Core.ThemePreference.System => ThemeKind.System, ToolSuite.Core.ThemePreference.Light => ThemeKind.Light, _ => ThemeKind.Dark });
    }

    private HotkeyCombo ParsedToggle() =>
        AccelCodec.Parse(_settings.Shortcut) ?? HotkeyPlan.SummonDefault;

    // —— 换键捕获编排（F31） ——

    /// <summary>
    /// 进入捕获：状态机退输入态+注销全部全局键 → 呼出面板+覆盖层 → 聚焦面板收键
    /// （捕获态 NeedsFocus=false 是状态机的键位语义；覆盖层的键盘采集由编排侧显式聚焦）。
    /// </summary>
    private void EnterShortcutCapture()
    {
        if (_panel is null || !_coordinator!.EnterInput(PanelMode.ShortcutCapture))
        {
            return;
        }
        _diagnostics.Vital("summon-req src=shortcut-capture");
        _diagnostics.Vital("summon-run src=shortcut-capture latency_ms=0");
        _panel.Summon();
        _panel.ShowCaptureOverlay();
        FocusAdapter.SetNoActivate(_panel, on: false);
        FocusAdapter.ActivateForInput(_panel);
    }

    /// <summary>覆盖层录入：校验（缺修饰）→ 试注册（占用/无效保旧键）→ 成功持久化 1200ms 收层。</summary>
    private void OnCaptureAttempted(HotkeyCombo combo)
    {
        var verdict = CaptureKeyRules.Validate(combo);
        if (verdict != CaptureVerdict.Ok)
        {
            _panel?.SetCaptureStatus(CaptureKeyRules.MessageOf(verdict), ok: false);
            return;
        }
        try { SetShortcut(AccelCodec.Encode(combo)); }
        catch (Exception ex) { _panel?.SetCaptureStatus(ex.Message, ok: false); return; }
        _coordinator!.ExitInput(PanelMode.ShortcutCapture, restoreFocus: false);
        _panel?.SetCaptureStatus($"已设置为 {combo.DisplayName}", ok: true);
        ScheduleCaptureOverlayClose();
    }

    /// <summary>Esc 取消：状态机退捕获态恢复旧键（差量自动恢复），capture-end 事件收层。</summary>
    private void OnCaptureCancelled()
    {
        _coordinator!.ExitInput(PanelMode.ShortcutCapture, restoreFocus: false);
        // ExitInputInternal 会发 CaptureEnd → SendCaptureEnd 收层；这里无需重复
    }

    /// <summary>成功路径的 1200ms 收层（legacy 渲染层 setTimeout；失败不收层可继续录）。</summary>
    private void ScheduleCaptureOverlayClose()
    {
        _captureSuccessTimer?.Stop();
        _captureSuccessTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _captureSuccessTimer.Tick += (_, _) =>
        {
            _captureSuccessTimer!.Stop();
            _captureSuccessTimer = null;
            _panel?.HideCaptureOverlay();
        };
        _captureSuccessTimer.Start();
    }

    // —— 呼出/停靠编排 ——

    /// <summary>
    /// 呼出时序（legacy show_on capture=true）：先拍焦点快照（失败静默）→ 协调器 Show
    /// （重置搜索/备注态 + 键位差量 + panel:shown 呼出重置）→ 面板几何落地。
    /// </summary>
    private void SummonPanel(string source, long requestedAt)
    {
        _diagnostics.Vital($"summon-run src={source} latency_ms={Stopwatch.GetElapsedTime(requestedAt).TotalMilliseconds:F1}");
        if (_panel is null)
        {
            _diagnostics.Vital("summon-skipped reason=not-ready");
            return;
        }
        if (_panel.IsDocked)
        {
            _coordinator!.EnsureFocusSnapshot();
            _coordinator.Show();
        }
        else
        {
            // 可见意图不代表未被遮挡；托盘/第二实例再次呼出仍刷新几何与置顶，保留输入态和焦点快照。
            _diagnostics.Vital("summon-refresh reason=already-visible");
        }
        _panel.Summon();
    }

    private void RequestSummon(string source)
    {
        var requestedAt = Stopwatch.GetTimestamp();
        _diagnostics.Vital($"summon-req src={source}");
        if (Dispatcher.CheckAccess())
        {
            SummonPanel(source, requestedAt);
            return;
        }
        try
        {
            Dispatch(() => SummonPanel(source, requestedAt));
        }
        catch (Exception exception)
        {
            _diagnostics.Vital($"dispatch-failed action=summon src={source} type={exception.GetType().FullName}");
        }
    }

    /// <summary>
    /// 停靠编排（Esc 停靠 / 呼出键收起 / 粘贴成功收起共用）：先退输入态并注销导航键，
    /// 再几何落位；restoreFocus=true 时快照由协调器归还并消费。
    /// </summary>
    private void DockPanel(bool restoreFocus, string reason)
    {
        if (_panel is not { IsDocked: false })
        {
            return;
        }
        _coordinator!.Hide(restoreFocus);
        _panel.Dock();
        var readable = WindowPlacer.TryGetPhysicalRect(_panel.Hwnd, out var actual);
        _diagnostics.Vital($"hide reason={reason} restore_focus={restoreFocus} readable={readable} " +
            $"actual={actual.Left},{actual.Top},{actual.Right},{actual.Bottom}");
    }

    /// <summary>浏览态 Esc 停靠（宿主事件入口）。</summary>
    private void DockFromPanel() => DockPanel(restoreFocus: true, reason: "escape");

    /// <summary>
    /// 单击面板外部 → 停靠（F17）：全局钩子上报的一次真实按下。判定全部在 Domain 纯规则
    /// （ExternalClickRules.ShouldDockOnOutsideClick）：面板可见 + 点击晚于本次呼出
    /// （时间窗防护：呼出瞬间的惯用手势不误收）+ 明确落在面板物理矩形外（矩形读不到不动作）。
    /// 输入态不豁免（legacy hide 统一路径逐层退出）；多击连点每次按下判一次（legacy 口径）。
    /// </summary>
    private void DockIfClickedOutside(int x, int y, long clickedAtMs)
    {
        if (_panel is not { IsDocked: false } panel
            || _coordinator is not { Visible: true } coordinator)
        {
            return; // 面板不可见：不动作
        }
        bool? inside = WindowPlacer.TryGetPhysicalRect(panel.Hwnd, out var rect)
            ? ExternalClickRules.ContainsPoint(rect.Left, rect.Top, rect.Right, rect.Bottom, x, y)
            : null;
        if (!ExternalClickRules.ShouldDockOnOutsideClick(
                panelVisible: true, coordinator.ShownAtMs, clickedAtMs, inside))
        {
            return;
        }
        DockPanel(restoreFocus: true, reason: "outside-click");
    }

    private void EnterSearch() => _ = _coordinator!.EnterInput(PanelMode.Search);

    /// <summary>
    /// 备注保存（F07）：编辑器侧只 trim，对比与写库都在服务侧（事实源）；空串等同移除。
    /// 保存即退输入态（legacy endNoteEdit → 主进程 exit_input：面板回浏览态并归还前台；
    /// 强退路径协调器已先退，此处 no-op）。成功 toast「备注已保存（dim 前 18 字符）」/
    /// 「备注已移除」，失败提示（F07 保存失败提示）。
    /// </summary>
    private void SaveNote(string id, string rawDraft)
    {
        _coordinator!.ExitInput(PanelMode.NoteEdit, restoreFocus: true);
        var text = rawDraft.Trim();
        var previous = _history!.Find(id)?.Note ?? string.Empty;
        if (text == previous)
        {
            return; // 无差异不写库（legacy finishNoteEditing）
        }
        if (_history.SetNote(id, text))
        {
            _panel?.ShowToast(
                text.Length > 0 ? "备注已保存" : "备注已移除",
                text.Length > 0 ? CompressNoteDim(text) : null,
                isError: false, actionLabel: null, onAction: null);
        }
        else
        {
            _panel?.ShowToast("备注保存失败", null, isError: true, actionLabel: null, onAction: null);
        }
    }

    /// <summary>保存成功 toast 的次级说明（legacy dim：text.slice(0, 18)）。</summary>
    private static string CompressNoteDim(string text) =>
        text.Length > 18 ? text[..18] : text;

    /// <summary>热键触发：呼出走停靠编排，导航动作按「真实按下」分发并武装长按连发（F19）。</summary>
    private void OnKeyAction(PanelKeyAction action)
    {
        if (_stopping) return;
        if (action.Kind == PanelKeyKind.Toggle)
        {
            if (_panel is { IsDocked: true })
            {
                RequestSummon("hotkey");
            }
            else
            {
                DockPanel(restoreFocus: true, reason: "hotkey-toggle");
            }
        }
        else
        {
            _coordinator?.OnNavPressed(action.NavAction);
        }
    }

    private void RequestPaste(string id)
    {
        if (_panel is not { IsDocked: false })
        {
            return; // 停靠态无粘贴意图
        }
        // 链路整体进模式执行线程（本仓库 ADR-0004）：UI 只发具名意图、渲染事实
        var generation = _generation;
        var paste = _paste;
        _executor?.Post(() =>
        {
            if (_stopping || generation != _generation) return;
            var result = paste!.Paste(id);
            Dispatch(() => { if (!_stopping && generation == _generation) _panel?.ShowResult(result); });
        });

    }
    private void HidePanelAfterPaste()
    {
        // 粘贴已把焦点归还原窗口，隐藏时不再重复恢复（F12）；归队 UI 线程执行停靠
        Dispatch(() => DockPanel(restoreFocus: false, reason: "paste-success"));
    }

    private void ShowStatus(string message)
    {
        Dispatch(() => _panel?.ShowStatus(message));
    }

    /// <summary>
    /// 四态状态机的效果宿主（IPanelModesHost 实现）。全部效果都在 UI 线程上执行
    /// （协调器线程封闭保证），窗与执行者的调用不再各自投递。
    /// </summary>
    private sealed class PanelModesHost(ClipboardModule app) : IPanelModesHost
    {
        public bool RegisterKey(HotkeyCombo combo, PanelKeyAction action)
        {
            app._hotkeys!.ApplyPlan(
                new HotkeyDiff([combo], []),
                _ => app.OnKeyAction(action));
            var registered = app._hotkeys.EffectiveKeys.Contains(combo);
            var message = $"hotkey_register accel={AccelCodec.Encode(combo)} ok={registered}";
            if (action.Kind == PanelKeyKind.Toggle)
            {
                app._diagnostics.Vital(message);
            }
            else
            {
                app._diagnostics.Verbose(message);
            }
            return registered;
        }

        public void UnregisterKey(HotkeyCombo combo) =>
            app._hotkeys!.ApplyPlan(new HotkeyDiff([], [combo]), _ => { });

        public IReadOnlyCollection<HotkeyCombo> CurrentKeys => app._hotkeys!.EffectiveKeys;

        public bool CanInteract() => app._panel is not null;

        public void FocusPanel()
        {
            if (app._panel is not { } panel)
            {
                return;
            }
            FocusAdapter.SetNoActivate(panel, on: false); // 输入态先清 NOACTIVATE（ADR-0002）
            FocusAdapter.ActivateForInput(panel);
        }

        public void BlurPanelIfFocused()
        {
            if (app._panel is { } panel)
            {
                FocusAdapter.SetNoActivate(panel, on: true); // 浏览态常驻不抢前台
            }
        }

        public void SendPanelKey(string action, string? noteEntryId)
        {
            if (action == NavAction.Escape.ActionName())
            {
                // 浏览态 Esc = 完整停靠流程（退输入态/注销导航键/归还快照/几何落位）
                app.DockFromPanel();
                return;
            }
            app._panel?.HandlePanelKey(action, noteEntryId);
        }

        public void SendPanelShown() => app._panel?.HandlePanelShown();

        public void SendCaptureEnd()
        {
            // capture-end：捕获取消/强退路径收覆盖层（成功路径由 1200ms 计时自行收）
            app._panel?.HideCaptureOverlay();
        }

        public FocusTarget? CaptureFocus() => FocusPasteRestore.Capture();

        public void RestoreFocus(FocusTarget target)
        {
            // 输入态退出归还前台（不注入粘贴）；失败按焦点错误回报
            if (FocusPasteRestore.RestoreAndPaste(target, paste: false) is { } failure)
            {
                app.ShowStatus(PasteChain.FocusErrorMessage(failure.Stage));
            }
        }

        public void ReportNoFocusTarget() =>
            app.ShowStatus(PasteChain.FocusErrorMessage(RestoreStage.Restore));

        public bool ValidateNoteTarget(string? targetId) =>
            targetId is null || app._history!.Find(targetId) is not null;

        public bool IsKeyDown(uint virtualKey) => KeyboardState.IsKeyDown(virtualKey);
    }

}

/// <summary>系统剪贴板边界；检查可注入隔离平台，不改动用户真实剪贴板。</summary>
public sealed record ClipboardPlatform(IClipboardReader Reader, IClipboardSequence Sequence,
    IClipboardWriter Writer, IForegroundSource Foreground, Func<IClipboardChangeEventSource> CreateSource);
