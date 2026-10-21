using ClipboardTool.Application;
using ClipboardTool.Domain.History;
using ClipboardTool.Domain.Hotkeys;
using ClipboardTool.Domain.PanelModes;
using ClipboardTool.Domain.Search;
using ClipboardTool.Infrastructure.Windows;
var root = args[0];
Directory.CreateDirectory(root);
var json = new JsonStore(root);
HistoryService OpenAt(string dir) { var h = new HistoryService(new HistoryStore(200,new ImageFileStore(dir),()=>Guid.NewGuid().ToString(),()=>1),new JsonStore(dir)); h.LoadFromStorage(); return h; }
static byte[] Png(byte r, byte g, byte b)
{
    var rgba = new byte[2 * 2 * 4];
    for (var i = 0; i < rgba.Length; i += 4)
    {
        rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = 0xFF;
    }
    return PngEncoder.Encode(2, 2, rgba);
}
var history = OpenAt(root);
history.RecordText("套件文字记录");
Directory.CreateDirectory(Path.Combine(root,"clipboard-history.json.tmp"));
try { history.SaveCurrent(); throw new Exception("保存拒绝时不得成功"); } catch(Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
Directory.Delete(Path.Combine(root,"clipboard-history.json.tmp"));
history.SaveCurrent();
if(OpenAt(root).Entries.Single().Text != "套件文字记录") throw new Exception("重启恢复文字失败");
Console.WriteLine("PASS 保存失败可观察、重试成功与独立数据重启恢复");


var reader = new FakeClipboard();
var recording = OpenAt(Path.Combine(root, "watch"));
using var watch = new ClipboardWatchService(reader, reader, recording, reader, reader);
var source = new FakeSource();
watch.Start(source);
watch.Dispose();
source.ReportReady(false);
reader.Text = "disabled must not capture";
reader.Sequence++;
await Task.Delay(750);
watch.PollRound();
if(recording.Entries.Any(e => e.Text == reader.Text)) throw new Exception("Disabled watcher captured content");
Console.WriteLine("PASS stop rejects late fallback and new changes");

// —— 图片记录：PNG 落盘到模块独立 images/、按像素身份去重、重启后可读回 ——
{
    var dir = Path.Combine(root, "image-basics");
    var pngA = Png(0x20, 0x80, 0xE0);
    var pngB = Png(0xE0, 0x40, 0x20);
    var store = OpenAt(dir);
    var source1 = new SourceApp("C:\\Windows\\notepad.exe", "Notepad", "草稿.txt - 记事本");
    store.RecordImage(pngA, source1);
    var first = store.Entries.Single();
    var file = Path.Combine(dir, "images", ImageFileNames.ForId(first.Id));
    if (!File.Exists(file)) throw new Exception("PNG 未落盘到模块 images/ 目录");
    if (DibDecoder.DecodePngPixels(File.ReadAllBytes(file)) is null) throw new Exception("落盘 PNG 无法解码");
    store.RecordImage(pngB);
    if (store.Entries.Count != 2) throw new Exception("第二张不同图片未新建条目");
    // 同像素重复复制：命中既有条目并提升，不新增文件
    store.RecordImage(pngA);
    if (store.Entries.Count != 2) throw new Exception("同图重复记录未去重");
    if (store.Entries[0].Id != first.Id) throw new Exception("同图重复记录未提升到普通块首");
    var files = Directory.GetFiles(Path.Combine(dir, "images"));
    if (files.Length != 2) throw new Exception("去重命中仍写出了多余图片文件");
    // 重启：图片条目、路径、来源、备注都可读回
    store.SetNote(first.Id, "截图备注");
    store.Promote(first.Id);
    var reopened = OpenAt(dir);
    if (reopened.Entries.Count != 2) throw new Exception("重启后图片条目丢失");
    var restored = reopened.Find(first.Id) ?? throw new Exception("重启后图片条目 id 丢失");
    if (restored.Type != EntryKind.Image || restored.ImagePath is null || !File.Exists(restored.ImagePath))
        throw new Exception("重启后图片路径不可用");
    if (restored.Note != "截图备注" || restored.SourceApp?.AppName != "Notepad")
        throw new Exception("重启后备注或来源丢失");
    // 文件丢失的图片条目在载入时被跳过（解码/文件缺失处理）
    File.Delete(restored.ImagePath);
    if (OpenAt(dir).Entries.Count != 1) throw new Exception("图片文件丢失后重启仍保留死条目");
    Console.WriteLine("PASS 图片 PNG 落盘独立目录、同像素去重不重写、重启后条目与来源备注可读");
}

// —— 文字与图片共用去重与 200 条上限，置顶不被裁剪 ——
{
    var dir = Path.Combine(root, "limit");
    var clock = 1_000L;
    var store = new HistoryService(
        new HistoryStore(HistoryStore.DefaultMaxHistory, new ImageFileStore(dir), () => Guid.NewGuid().ToString(), () => clock++),
        new JsonStore(dir));
    if (HistoryStore.DefaultMaxHistory != 200) throw new Exception("默认上限不是 200");
    for (var i = 0; i < 199; i++) store.RecordText($"条目{i:000}");
    var oldest = store.Entries[^1];
    store.TogglePin(oldest.Id);
    store.RecordText("新增一");
    store.RecordText("新增二");
    if (store.Entries.Count != 200) throw new Exception($"上限应为 200，实际 {store.Entries.Count}");
    if (store.Find(oldest.Id) is not { Pinned: true }) throw new Exception("置顶条目被上限裁剪");
    if (store.Entries.Any(e => e.Text == "条目001")) throw new Exception("裁剪未先裁最旧普通条目");
    if (store.Entries.All(e => e.Text != "新增二")) throw new Exception("最新条目未保留");

    // 图片与文字同处一个上限；置顶图片不被裁剪，普通图片随裁剪删除文件
    var smallDir = Path.Combine(root, "limit-image");
    var small = new HistoryService(
        new HistoryStore(3, new ImageFileStore(smallDir), () => Guid.NewGuid().ToString(), () => clock++),
        new JsonStore(smallDir));
    small.RecordImage(Png(0x11, 0x22, 0x33));
    var pinnedImage = small.Entries.Single();
    if (!small.TogglePin(pinnedImage.Id)) throw new Exception("图片置顶失败");
    small.RecordText("a"); small.RecordText("b"); small.RecordText("c");
    if (small.Entries.Count != 3) throw new Exception("图片与文字合计未按同一上限裁剪");
    if (small.Find(pinnedImage.Id) is not { Pinned: true }) throw new Exception("置顶图片被裁剪");
    if (!File.Exists(pinnedImage.ImagePath!)) throw new Exception("置顶图片文件被删除");

    var trimDir = Path.Combine(root, "limit-trim-image");
    var trim = new HistoryService(
        new HistoryStore(2, new ImageFileStore(trimDir), () => Guid.NewGuid().ToString(), () => clock++),
        new JsonStore(trimDir));
    trim.RecordImage(Png(0x44, 0x55, 0x66));
    var dropPath = trim.Entries.Single().ImagePath!;
    trim.RecordText("x"); trim.RecordText("y");
    if (trim.Entries.Any(e => e.Type == EntryKind.Image)) throw new Exception("超限图片条目未裁剪");
    if (File.Exists(dropPath)) throw new Exception("裁剪未删除对应 PNG 文件");
    Console.WriteLine("PASS 文字与图片共用 200 条上限（含置顶不裁剪）并按规则联动删除图片文件");
}

// —— 来源显示所需字段与正文/备注/来源多关键词搜索 ——
{
    var dir = Path.Combine(root, "search");
    var store = OpenAt(dir);
    var sourceApp = new SourceApp("C:\\Windows\\notepad.exe", "Notepad", "report.txt - 记事本");
    store.RecordText("季度 报告 草稿", sourceApp);
    var reportId = store.Entries.Single().Id;
    store.SetNote(reportId, "待办 复核");
    store.RecordText("无关内容");
    var all = store.Entries;

    IReadOnlyList<HistoryEntry> Find(string query) => SearchRules.FilterEntries(all, query);
    if (Find("报告 复核").SingleOrDefault()?.Id != reportId) throw new Exception("正文+备注多词 AND 未命中");
    if (Find("NOTEPAD 报告").SingleOrDefault()?.Id != reportId) throw new Exception("来源应用名未参与搜索");
    if (Find("report.txt").SingleOrDefault()?.Id != reportId) throw new Exception("来源窗口标题未参与搜索");
    if (Find("notepad.exe").SingleOrDefault()?.Id != reportId) throw new Exception("来源 exe 路径未参与搜索");
    if (Find("报告 不存在").Count != 0) throw new Exception("多词 AND 被放宽为 OR");
    if (Find("  ").Count != 2) throw new Exception("空查询应返回全量");
    var spans = SearchRules.Highlight("季度 报告 草稿", "报告");
    if (SearchRules.SpansToText(spans) != "季度 报告 草稿") throw new Exception("高亮丢字符");
    if (!spans.Any(span => span is { Hit: true, Text: "报告" })) throw new Exception("正文命中未高亮");
    Console.WriteLine("PASS 正文/备注/来源（应用名、窗口标题、exe 路径）多关键词 AND 搜索与高亮");
}

// —— 键位模式：浏览/搜索/备注编辑/快捷键捕获的让位矩阵 ——
{
    var toggle = AccelCodec.Parse("Control+Shift+V")!.Value;
    var browse = PanelKeySets.DesiredKeys(visible: true, PanelMode.Browse, composing: false, toggle);
    if (browse.Count != 9) throw new Exception($"浏览态应注册呼出键+8 个导航键，实际 {browse.Count}");
    var actions = browse.Select(b => b.Action.NavAction).ToHashSet();
    foreach (var expected in new[] { NavAction.Up, NavAction.Down, NavAction.Enter, NavAction.Escape, NavAction.Delete, NavAction.Pin, NavAction.Note, NavAction.Search })
    {
        if (!actions.Contains(expected)) throw new Exception($"浏览态缺少 {expected} 键位");
    }
    var search = PanelKeySets.DesiredKeys(true, PanelMode.Search, composing: false, toggle);
    var searchActions = search.Where(b => b.Action.Kind == PanelKeyKind.Nav).Select(b => b.Action.NavAction).ToHashSet();
    if (!searchActions.SetEquals([NavAction.Up, NavAction.Down, NavAction.Enter, NavAction.Escape]))
        throw new Exception("搜索态应只保留上下/Enter/Esc，字符键让位给输入框");
    if (PanelKeySets.DesiredKeys(true, PanelMode.Search, composing: true, toggle).Count != 1)
        throw new Exception("IME 组合期间导航键应全部让位");
    if (PanelKeySets.DesiredKeys(true, PanelMode.NoteEdit, composing: false, toggle).Count != 1)
        throw new Exception("备注编辑态应只留呼出键");
    if (PanelKeySets.DesiredKeys(true, PanelMode.ShortcutCapture, composing: false, toggle).Count != 0)
        throw new Exception("捕获态应注销包括呼出键在内的全部全局键");
    if (PanelKeySets.DesiredKeys(false, PanelMode.Browse, composing: false, toggle).Count != 1)
        throw new Exception("面板收起时只应保留呼出键");
    Console.WriteLine("PASS 浏览/搜索/备注/捕获四态默认键位矩阵与收起态让位");
}

// —— 置顶/取消置顶、备注、删除 6 秒撤销与到期提交 ——
{
    var dir = Path.Combine(root, "undo");
    var store = OpenAt(dir);
    store.RecordImage(Png(0x70, 0x80, 0x90));
    var imageId = store.Entries.Single().Id;
    var imagePath = store.Entries.Single().ImagePath!;
    var scheduler = new ManualScheduler();
    using var deletion = new PendingDeletionService(scheduler, store);
    var toasts = new List<PendingDeletionToast>();
    deletion.ToastRequested += toasts.Add;

    deletion.Request(imageId);
    if (!deletion.HiddenIds.Contains(imageId)) throw new Exception("删除请求未从可见列表摘除");
    if (store.Find(imageId) is null) throw new Exception("撤销窗口内不应真正删除");
    if (scheduler.RequestedMs != PendingDeletionService.UndoWindowMs || PendingDeletionService.UndoWindowMs != 6000)
        throw new Exception("撤销窗口不是 6 秒");
    if (toasts.SingleOrDefault() is not { ActionLabel: "撤销" } toast || toast.Dim is null)
        throw new Exception("删除后缺少撤销提示或次级说明");

    deletion.Undo(imageId);
    if (deletion.HiddenIds.Count != 0 || scheduler.Pending != 0) throw new Exception("撤销未恢复显示或未取消计时");
    if (store.Find(imageId) is null) throw new Exception("撤销后条目丢失");

    deletion.Request(imageId);
    scheduler.FireAll();
    if (store.Find(imageId) is not null) throw new Exception("到期后未提交删除");
    if (File.Exists(imagePath)) throw new Exception("到期删除未清理图片文件");
    if (deletion.HiddenIds.Contains(imageId)) throw new Exception("到期后仍残留摘除状态");

    // 备注与置顶（取消置顶保留 pinnedAt，条目回普通块首）
    store.RecordText("备注目标");
    var noteId = store.Entries[0].Id;
    if (!store.SetNote(noteId, "  前后空白  ")) throw new Exception("备注保存失败");
    if (store.Find(noteId)!.Note != "前后空白") throw new Exception("备注未归一化");
    store.TogglePin(noteId);
    var pinnedAt = store.Find(noteId)!.PinnedAtMs;
    store.TogglePin(noteId);
    var unpinned = store.Find(noteId)!;
    if (unpinned.Pinned || unpinned.PinnedAtMs != pinnedAt) throw new Exception("取消置顶未保留 pinnedAt");
    if (store.Entries[0].Id != noteId) throw new Exception("取消置顶未回到普通块首");
    Console.WriteLine("PASS 置顶/取消置顶、备注归一化、删除摘除与 6 秒撤销/到期提交");
}

// —— 清空历史：图片文件、待删除计时、反馈与重启结果 ——
{
    var dir = Path.Combine(root, "clear");
    var store = OpenAt(dir);
    store.RecordImage(Png(0xA0, 0xB0, 0xC0));
    var pendingId = store.Entries[0].Id;
    store.RecordText("文字条目", new SourceApp("", "Notepad", ""));
    store.TogglePin(store.Entries[0].Id); // 置顶也一起清
    var scheduler = new ManualScheduler();
    using var deletion = new PendingDeletionService(scheduler, store);
    var toasts = new List<PendingDeletionToast>();
    deletion.ToastRequested += toasts.Add;
    deletion.Request(pendingId);
    if (scheduler.Pending != 1) throw new Exception("待删除计时未建立");

    deletion.ClearAll();
    if (store.Entries.Count != 0) throw new Exception("清空后仍有条目");
    if (deletion.HiddenIds.Count != 0 || scheduler.Pending != 0) throw new Exception("清空后仍残留删除摘除或计时");
    var imagesDir = Path.Combine(dir, "images");
    if (Directory.Exists(imagesDir) && Directory.GetFiles(imagesDir).Length != 0)
        throw new Exception("清空后 images/ 仍有残留图片");
    scheduler.FireAll(); // 已取消的计时不应再触发
    if (toasts.Any(t => t.Message.Contains("失败"))) throw new Exception("清空后残留计时误报删除失败");
    var reopened = OpenAt(dir);
    if (reopened.Entries.Count != 0) throw new Exception("清空结果未落盘，重启后又出现条目");
    var archive = new JsonStore(dir).ReadHistory();
    if (archive is not HistoryFileRead.Loaded loaded || loaded.Json.Trim() != "[]")
        throw new Exception("清空后存档不是空数组");
    Console.WriteLine("PASS 清空历史联动删除图片、取消待删除计时且重启后为空");
}

sealed class ManualScheduler : IDelayScheduler
{
    private readonly List<ManualTimer> _timers = [];
    public int RequestedMs { get; private set; }
    public int Pending => _timers.Count;
    public IDisposable Delay(int milliseconds, Action callback)
    {
        RequestedMs = milliseconds;
        var timer = new ManualTimer(this, callback);
        _timers.Add(timer);
        return timer;
    }
    public void FireAll()
    {
        foreach (var timer in _timers.ToArray())
        {
            _timers.Remove(timer);
            timer.Fire();
        }
    }
    private sealed class ManualTimer(ManualScheduler owner, Action callback) : IDisposable
    {
        public void Fire() => callback();
        public void Dispose() => owner._timers.Remove(this);
    }
}

sealed class FakeClipboard : IClipboardReader,IClipboardWriter,IClipboardSequence,IForegroundSource
{
    public string Text = ""; public uint Sequence = 1;
    public ClipboardReadOutcome Read() => new ClipboardReadOutcome.Known(new ClipboardSnapshot(Text,null));
    public uint Current() => Sequence;
    public bool WriteText(string text) { Text=text; Sequence++; return true; }
    public bool WriteImage(string path)=>false;
    public SourceApp? Capture()=>null;
}
sealed class FakeSource : IClipboardChangeEventSource
{
    public Func<bool>? RoundHandler {get;set;}
    Action<bool>? ready;
    public void Start(Action<bool> ready) => this.ready=ready;
    public void ReportReady(bool success) => ready?.Invoke(success);
}
