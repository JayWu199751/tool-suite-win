using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ToolSuite.Core;

namespace ToolSuite.Reminders;

public sealed class ReminderModule : IToolModule
{
    private ModuleContext? context;
    private ReminderSchedule? schedule;
    private DispatcherTimer? timer;
    private ReminderWindow? window;
    private Dispatcher? dispatcher;
    private bool sessionLocked;
    private readonly Dictionary<Guid, ReminderEditorWindow> editors = [];
    public string Id => ToolIds.Reminders;
    public string DisplayName => "循环提醒";
    public ThemePreference Theme => context?.Theme ?? ThemePreference.Light;
    public event Action? AppearanceChanged;
    public ReminderSchedule Schedule => schedule ??= new ReminderSchedule(
        context?.DataDirectory ?? throw new InvalidOperationException("提醒工具尚未配置。"), ReminderDesktop.ElapsedMilliseconds);
    public IReadOnlyList<ToolCommand> Commands => context is null ? [] :
        [new("new", "新增提醒…", token =>
        {
            token.ThrowIfCancellationRequested();
            OpenEditor();
            return Task.CompletedTask;
        }, RequiresRunning: false),
        new("settings", "管理循环提醒…", token =>
        {
            token.ThrowIfCancellationRequested();
            context.RequestSettings?.Invoke(Id);
            return Task.CompletedTask;
        }, RequiresRunning: false)];

    public void Configure(ModuleContext value) => context = value;

    public void OpenEditor(Guid? reminderId = null)
    {
        var key = reminderId ?? Guid.Empty;
        if (editors.TryGetValue(key, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var reminder = reminderId is { } id ? Schedule.Reminders.FirstOrDefault(r => r.Id == id)
            ?? throw new InvalidOperationException("这项提醒已被删除，请刷新列表后重试。") : null;
        var editor = new ReminderEditorWindow(this, reminder);
        editors.Add(key, editor);
        editor.Closed += (_, _) => editors.Remove(key);
        try { editor.Show(); }
        catch { editors.Remove(key); editor.Close(); throw; }
    }

    public Task StartAsync(ModuleContext value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Configure(value);
        if (timer is not null) return Task.CompletedTask;
        dispatcher = Dispatcher.CurrentDispatcher;
        sessionLocked = !ReminderDesktop.CanShowReminder();
        Schedule.Start();
        Schedule.Changed += OnScheduleChanged;
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += OnTick;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        timer.IsEnabled = Schedule.Reminders.Any(r => r.Enabled);
        return Task.CompletedTask;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // 计时与呈现分开：锁屏仍到期，解锁后只显示一次。
        Schedule.Tick();
        RefreshPopup();
    }

    private void OnScheduleChanged()
    {
        if (timer is not null) timer.IsEnabled = Schedule.IsRunning && Schedule.Reminders.Any(r => r.Enabled);
        RefreshPopup();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock
            or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.RemoteConnect
            or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.ConsoleConnect)) return;
        var locked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect;
        dispatcher?.BeginInvoke(() => { sessionLocked = locked; if (timer is not null) OnTick(null, EventArgs.Empty); });
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => dispatcher?.BeginInvoke(() => window?.Reposition());
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
        => dispatcher?.BeginInvoke(() => ApplyTheme(Theme));

    private void RefreshPopup()
    {
        if (!Schedule.IsRunning || Schedule.Pending.Count == 0)
        {
            var previous = window; window = null;
            previous?.DismissSilently();
            return;
        }
        var available = ReminderDesktop.CanShowReminder();
        // 输入桌面回到 Default 也可恢复启动时已锁屏的状态。
        if (sessionLocked && available) sessionLocked = false;
        if (sessionLocked || !available) { window?.Hide(); return; }
        if (window is null)
        {
            var created = new ReminderWindow(Schedule, Theme);
            window = created;
            created.Dismissed += silent =>
            {
                if (ReferenceEquals(window, created)) window = null;
                if (!silent) Schedule.Acknowledge(Schedule.Pending.Select(r => r.Id));
            };
        }
        window.UpdatePending();
        if (!window.IsVisible) window.Show();
    }

    public Task PrepareToStopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 所有配置编辑都是先持久化再生效，没有待保存的运行时数据。
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (timer is not null) { timer.Stop(); timer.Tick -= OnTick; timer = null; }
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        if (schedule is not null) { schedule.Changed -= OnScheduleChanged; schedule.Stop(); }
        var previous = window; window = null; previous?.DismissSilently();
        foreach (var editor in editors.Values.ToArray()) editor.Close();
        return Task.CompletedTask;
    }

    public void ApplyTheme(ThemePreference theme)
    {
        if (context is not null) context = context with { Theme = theme };
        window?.ApplyTheme(theme);
        foreach (var editor in editors.Values) editor.ApplyTheme(theme);
        AppearanceChanged?.Invoke();
    }
}
