using System.IO;
using System.Text.Json;

namespace ToolSuite.Reminders;

public sealed record Reminder(Guid Id, string Name, int IntervalSeconds, bool Enabled);

/// <summary>配置先落盘再生效；仅本次运行保存截止点，确认后才开始下一轮。
/// Windows 的 TickCount64 包含睡眠时间，不受系统时钟或时区调整影响。</summary>
public sealed class ReminderSchedule
{
    private readonly string path;
    private readonly Func<long> milliseconds;
    private readonly Dictionary<Guid, long> deadlines = [];
    private readonly HashSet<Guid> pending = [];
    private List<Reminder>? reminders;
    public event Action? Changed;
    public bool IsRunning { get; private set; }
    public IReadOnlyList<Reminder> Reminders { get { Load(); return reminders!.AsReadOnly(); } }
    public IReadOnlyList<Reminder> Pending => Reminders.Where(r => pending.Contains(r.Id)).ToArray();

    public ReminderSchedule(string directory, Func<long>? milliseconds = null)
    {
        path = Path.Combine(directory, "reminders.json");
        this.milliseconds = milliseconds ?? ReminderDesktop.ElapsedMilliseconds;
    }

    private void Load()
    {
        if (reminders is not null) return;
        var loaded = File.Exists(path)
            ? JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("提醒数据为空，请检查 reminders.json，原文件已保留。")
            : [];
        if (loaded.Any(r => r is null || r.Id == Guid.Empty || string.IsNullOrWhiteSpace(r.Name)
            || r.Name.Length > 120 || r.IntervalSeconds <= 0)
            || loaded.Select(r => r.Id).Distinct().Count() != loaded.Count)
            throw new InvalidDataException("提醒数据包含无效内容，原文件已保留，请检查 reminders.json。");
        reminders = loaded;
    }

    public void Start()
    {
        Load();
        if (IsRunning) return;
        var now = milliseconds();
        deadlines.Clear(); pending.Clear();
        foreach (var item in reminders!.Where(r => r.Enabled)) deadlines[item.Id] = Deadline(item, now);
        IsRunning = true;
        Changed?.Invoke();
    }

    public void Stop()
    {
        IsRunning = false; deadlines.Clear(); pending.Clear();
        Changed?.Invoke();
    }

    public Reminder Save(Guid? id, string name, int intervalSeconds, bool enabled)
    {
        Load();
        name = name.Trim();
        if (name.Length is 0 or > 120) throw new ArgumentException("请输入 1～120 个字符的提醒名称。");
        if (intervalSeconds <= 0) throw new ArgumentException("提醒间隔必须至少为 1 秒。");
        var previous = id is { } existing ? reminders!.Single(r => r.Id == existing) : null;
        var item = new Reminder(previous?.Id ?? Guid.NewGuid(), name, intervalSeconds, enabled);
        var next = reminders!.ToList();
        if (previous is null) next.Add(item); else next[next.IndexOf(previous)] = item;
        Persist(next);
        reminders = next;
        if (!enabled || !IsRunning) { deadlines.Remove(item.Id); pending.Remove(item.Id); }
        else if (previous is null || !previous.Enabled || previous.IntervalSeconds != intervalSeconds)
        {
            pending.Remove(item.Id);
            deadlines[item.Id] = Deadline(item, milliseconds());
        }
        Changed?.Invoke();
        return item;
    }

    public void Delete(Guid id)
    {
        Load();
        var next = reminders!.Where(r => r.Id != id).ToList();
        Persist(next);
        reminders = next; deadlines.Remove(id); pending.Remove(id);
        Changed?.Invoke();
    }

    public void Tick()
    {
        if (!IsRunning) return;
        var now = milliseconds();
        var due = deadlines.Where(d => d.Value <= now).Select(d => d.Key).ToArray();
        if (due.Length == 0) return;
        foreach (var id in due) { deadlines.Remove(id); pending.Add(id); }
        Changed?.Invoke();
    }

    public TimeSpan? Remaining(Guid id) => deadlines.TryGetValue(id, out var end)
        ? TimeSpan.FromMilliseconds(Math.Max(0, end - milliseconds())) : null;

    public void Acknowledge(IEnumerable<Guid> ids)
    {
        var now = milliseconds();
        foreach (var id in ids.ToArray())
            if (pending.Remove(id) && IsRunning && reminders!.FirstOrDefault(r => r.Id == id) is { Enabled: true } item)
                deadlines[id] = Deadline(item, now);
        Changed?.Invoke();
    }

    private static long Deadline(Reminder item, long now) => checked(now + item.IntervalSeconds * 1000L);

    private void Persist(List<Reminder> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, next, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
