using Pinto.Core;

var outputBase = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "artifacts", "data-checks"));
var root = Path.Combine(outputBase, $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
var passed = 0;
Run("首次运行与主动保留空集合", () =>
{
    var store = Store("first"); Check(store.Load().IsFirstRun, "首次标志"); store.Save(new());
    Check(!store.Load().IsFirstRun && store.Load().Data.Notes.Count == 0, "用户删除全部便签后不应自动重建");
});
Run("中文、完成状态、顺序与隐藏布局恢复", () =>
{
    var store = Store("roundtrip"); store.Save(Example()); var result = store.Load().Data; var note = result.Notes.Single();
    Check(result.Theme == ThemePreference.Dark && note.Title == "工作 · 中文", "主题或中文丢失");
    Check(!note.IsVisible && note.Left == -300 && note.Top == 124 && note.Width == 420, "布局丢失");
    Check(note.Items.Select(item => item.Text).SequenceEqual(new[] { "完成季度报告", "回复邮件\n补充附件" }) && note.Items[1].IsCompleted, "清单丢失");
});
Run("原子保存保留上一份快照", () =>
{
    var store = Store("atomic"); var data = Example(); store.Save(data); data.Notes[0].Title = "第二版"; store.Save(data);
    Check(store.ReadBackup(store.PreviousPath).Notes[0].Title == "工作 · 中文", "上一份快照错误");
    Check(!Directory.EnumerateFiles(store.DirectoryPath, "*.tmp").Any(), "遗留临时文件");
});
Run("损坏主文件恢复并保留原件", () =>
{
    var store = Store("recover"); var data = Example(); store.Save(data); data.Notes[0].Title = "较新版"; store.Save(data);
    File.WriteAllText(store.StatePath, "损坏内容"); var result = store.Load();
    Check(result.Notice is not null && result.Data.Notes[0].Title == "工作 · 中文", "未恢复快照");
    Check(File.ReadAllText(Directory.GetFiles(store.DirectoryPath, "state.corrupt-*.json").Single()) == "损坏内容", "损坏原件丢失");
    store.Save(result.Data); Check(store.ReadBackup(store.PreviousPath).Notes[0].Title == "工作 · 中文", "恢复来源被破坏");
});
Run("两个损坏文件均不覆盖", () =>
{
    var store = Store("double-corrupt"); Directory.CreateDirectory(store.DirectoryPath);
    File.WriteAllText(store.StatePath, "main-bad"); File.WriteAllText(store.PreviousPath, "previous-bad");
    Throws<InvalidDataException>(() => store.Load());
    Check(File.ReadAllText(store.StatePath) == "main-bad" && File.ReadAllText(store.PreviousPath) == "previous-bad", "原件被覆盖");
});
Run("主文件缺失时恢复上一份快照", () =>
{
    var store = Store("missing-main"); store.Save(Example()); store.Save(Example()); File.Delete(store.StatePath);
    Check(store.Load().Data.Notes[0].Title == "工作 · 中文", "未恢复");
});
Run("导出备份不修改主文件且禁止覆盖主文件", () =>
{
    var store = Store("export"); store.Save(Example()); var before = File.ReadAllBytes(store.StatePath);
    var backup = Path.Combine(root, "backup.json"); store.Export(Example(), backup);
    Check(store.ReadBackup(backup).Notes[0].Items[1].IsCompleted, "缺少完成状态");
    Check(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "主文件改变");
    Throws<InvalidDataException>(() => store.Export(Example(), store.StatePath));
});
Run("重复追加生成独立副本并保留当前主题", () =>
{
    var store = Store("append"); var current = Example(); current.Theme = ThemePreference.Light; store.Save(current);
    var file = Path.Combine(root, "append.json"); store.Export(Example(), file);
    var first = store.Import(current, file, ImportMode.Append); var second = store.Import(first.Data, file, ImportMode.Append);
    Check(current.Notes.Count == 1 && second.Data.Notes.Count == 3 && second.Data.Theme == ThemePreference.Light, "追加污染原状态或设置");
    Check(second.Data.Notes.Select(note => note.Id).Distinct().Count() == 3, "便签标识重复");
    Check(second.Data.Notes.SelectMany(note => note.Items).Select(item => item.Id).Distinct().Count() == 6, "清单项标识重复");
});
Run("替换前备份并保留当前主题", () =>
{
    var store = Store("replace"); var current = Example(); current.Theme = ThemePreference.Light; store.Save(current);
    var incoming = Example(); incoming.Notes[0].Title = "替换来源"; var file = Path.Combine(root, "replacement.json"); store.Export(incoming, file);
    var result = store.Import(current, file, ImportMode.Replace);
    Check(result.Data.Notes.Single().Title == "替换来源" && result.Data.Theme == ThemePreference.Light, "替换或主题错误");
    Check(result.SafetyBackupPath is not null && store.ReadBackup(result.SafetyBackupPath).Notes[0].Title == "工作 · 中文", "没有替换前备份");
    Check(store.Load().Data.Notes[0].Title == "替换来源" && current.Notes[0].Title == "工作 · 中文", "未持久化或污染原对象");
});
Run("无效导入不修改当前文件", () =>
{
    var store = Store("invalid-import"); var current = Example(); store.Save(current); var before = File.ReadAllBytes(store.StatePath);
    foreach (var text in new[] { "{}", "null", "{bad", "{\"format\":\"Pinto\",\"schemaVersion\":99,\"notes\":[]}", "{\"format\":\"Pinto\",\"schemaVersion\":1,\"notes\":null}" })
    {
        var file = Path.Combine(root, "invalid.json"); File.WriteAllText(file, text);
        Throws<Exception>(() => store.Import(current, file, ImportMode.Replace));
        Check(File.ReadAllBytes(store.StatePath).SequenceEqual(before), "失败导入改动了数据");
    }
    Check(!Directory.Exists(Path.Combine(store.DirectoryPath, "Backups")), "无效导入进入替换事务");
});
Run("超大备份在读取内容前拒绝", () =>
{
    var file = Path.Combine(root, "oversized.json"); using (var stream = File.Create(file)) stream.SetLength(DataStore.MaximumFileBytes + 1);
    Throws<InvalidDataException>(() => Store("oversized").ReadBackup(file));
});
Run("重复标识、空清单项和无效位置拒绝保存", () =>
{
    var store = Store("invalid-data"); var data = Example(); data.Notes.Add(data.Notes[0]); Throws<InvalidDataException>(() => store.Save(data));
    data = Example(); data.Notes[0].Items[1].Id = data.Notes[0].Items[0].Id; Throws<InvalidDataException>(() => store.Save(data));
    data = Example(); data.Notes[0].Items[0].Text = " "; Throws<InvalidDataException>(() => store.Save(data));
    data = Example(); data.Notes[0].Width = double.NaN; Throws<InvalidDataException>(() => store.Save(data));
    Check(!File.Exists(store.StatePath), "校验失败仍写入文件");
});
Run("导入写入失败保留调用方状态", () =>
{
    var store = Store("write-failure"); var current = Example(); Directory.CreateDirectory(store.StatePath);
    var file = Path.Combine(root, "valid-for-failure.json"); store.Export(Example(), file);
    Throws<IOException>(() => store.Import(current, file, ImportMode.Append));
    Check(current.Notes.Count == 1 && Directory.Exists(store.StatePath), "失败后原状态被改动");
    Check(!Directory.EnumerateFiles(store.DirectoryPath, "*.tmp").Any(), "失败后遗留临时文件");
});
Run("主动恢复同时保留损坏主文件与快照", () =>
{
    var store = Store("explicit-recovery"); Directory.CreateDirectory(store.DirectoryPath);
    File.WriteAllText(store.StatePath, "bad-main"); File.WriteAllText(store.PreviousPath, "bad-previous");
    var file = Path.Combine(root, "recovery.json"); store.Export(Example(), file); var data = store.RecoverFromBackup(file);
    Check(store.Load().Data.Notes[0].Title == data.Notes[0].Title, "恢复未落盘");
    Check(Directory.GetFiles(store.DirectoryPath, "*.corrupt-*").Length == 2, "未保留两份原件");
});

Run("四边及四角吸附保留窗口大小", () =>
{
    var work = new WindowBounds(0, 0, 1920, 1040);
    foreach (var edges in new[] { DockEdges.Left, DockEdges.Right, DockEdges.Top, DockEdges.Bottom,
        DockEdges.Left | DockEdges.Top, DockEdges.Right | DockEdges.Top, DockEdges.Left | DockEdges.Bottom, DockEdges.Right | DockEdges.Bottom })
    {
        var target = EdgeDocking.Align(new(400, 300, 324, 273), work, edges);
        var near = target with { Left = target.Left + (edges.HasFlag(DockEdges.Right) ? -7 : 7), Top = target.Top + (edges.HasFlag(DockEdges.Bottom) ? -7 : 7) };
        var result = EdgeDocking.Snap(near, work, 12);
        Check(result.Edges == edges, $"方向错误：{edges}");
        Check(result.Bounds == EdgeDocking.Align(near, work, edges) && result.Bounds.Width == 324 && result.Bounds.Height == 273, "吸附改变了大小");
    }
});
Run("拖离解除吸附且支持负坐标和不同缩放阈值", () =>
{
    var work = new WindowBounds(-1920, -100, 1920, 1040);
    var bounds = new WindowBounds(-1600, 100, 324, 273);
    Check(EdgeDocking.Snap(bounds, work, 12) == new DockingResult(bounds, DockEdges.None), "未解除贴边");
    var near = bounds with { Left = work.Left + 16, Top = work.Top + 16 };
    Check(EdgeDocking.Snap(near, work, 12).Edges == DockEdges.None, "超出阈值仍吸附");
    var scaled = EdgeDocking.Snap(near, work, 18);
    Check(scaled.Edges == (DockEdges.Left | DockEdges.Top) && scaled.Bounds.Left == -1920 && scaled.Bounds.Top == -100, "负坐标或缩放阈值错误");
});
Run("缩放吸附只调整对应边且记忆适应工作区变化", () =>
{
    var work = new WindowBounds(0, 0, 1920, 1040);
    var result = EdgeDocking.Snap(new(1500, 700, 414, 334), work, 12, resizing: true);
    Check(result.Edges == (DockEdges.Right | DockEdges.Bottom) && result.Bounds == new WindowBounds(1500, 700, 420, 340), "缩放平移了未拖动的边");
    var moved = EdgeDocking.Align(new(1500, 700, 324, 273), new(0, 0, 1600, 860), result.Edges);
    Check(moved.Right == 1600 && moved.Bottom == 860 && moved.Width == 324, "工作区变化后丢失贴边");
});
Run("贴边记忆保存恢复且兼容旧数据", () =>
{
    var store = Store("docking"); var data = Example(); data.Notes[0].DockEdges = DockEdges.Right | DockEdges.Bottom;
    store.Save(data); var note = store.Load().Data.Notes[0];
    Check(note.DockEdges == data.Notes[0].DockEdges && !note.IsVisible && note.Width == 420, "贴边或隐藏布局丢失");
    var legacy = Store("legacy-docking"); Directory.CreateDirectory(legacy.DirectoryPath);
    File.WriteAllText(legacy.StatePath, "{\"format\":\"Pinto\",\"schemaVersion\":1,\"notes\":[{\"title\":\"旧便签\"}]}");
    Check(legacy.Load().Data.Notes[0].DockEdges == DockEdges.None, "旧数据未默认自由移动");
    foreach (var invalid in new[] { DockEdges.Left | DockEdges.Right, DockEdges.Top | DockEdges.Bottom, (DockEdges)16 })
    {
        data.Notes[0].DockEdges = invalid;
        Throws<InvalidDataException>(() => store.Save(data));
    }
});

Run("便签四向相接并自动对齐邻边", () =>
{
    var work = new WindowBounds(0, 0, 1920, 1040);
    var peer = new WindowBounds(600, 350, 300, 220);
    foreach (var (near, expected) in new[]
    {
        (new WindowBounds(394, 355, 200, 160), new WindowBounds(400, 350, 200, 160)),
        (new WindowBounds(906, 355, 200, 160), new WindowBounds(900, 350, 200, 160)),
        (new WindowBounds(605, 184, 200, 160), new WindowBounds(600, 190, 200, 160)),
        (new WindowBounds(605, 576, 200, 160), new WindowBounds(600, 570, 200, 160))
    }) Check(NoteDocking.Snap(near, work, [peer], 12) == expected, "相接方向或邻边对齐错误");
});
Run("便签贴边选择最近边且忽略远处便签", () =>
{
    var work = new WindowBounds(0, 0, 1920, 1040);
    var bounds = new WindowBounds(407, 330, 200, 160);
    var peers = new[] { new WindowBounds(600, 300, 300, 220), new WindowBounds(610, 300, 300, 220) };
    Check(NoteDocking.Snap(bounds, work, peers, 12).Right == 610, "没有选择最近的边");
    Check(NoteDocking.Snap(bounds, work, [new(600, 900, 300, 100)], 12) == bounds, "吸附到纵向远离的便签");
    Check(NoteDocking.Snap(bounds, work, [], 12) == bounds, "没有相邻便签时改变位置");
    var screen = new WindowBounds(0, 330, 200, 160);
    Check(NoteDocking.Snap(screen, work, [new(-290, 300, 300, 220)], 12, DockEdges.Left).Left == 0, "覆盖了屏幕贴边");
});
Run("便签相接支持双边排列、负坐标与拖离", () =>
{
    var work = new WindowBounds(0, 0, 1920, 1040);
    var combined = NoteDocking.Snap(new(394, 295, 200, 160), work, [new(100, 320, 300, 220), new(420, 80, 300, 220)], 12);
    Check(combined.Left == 400 && combined.Top == 300, "未同时吸附两张便签的相邻边");
    var negativeWork = new WindowBounds(-1920, -500, 1920, 1040);
    var peer = new WindowBounds(-800, -200, 300, 220);
    var near = new WindowBounds(-484, -194, 200, 160);
    Check(NoteDocking.Snap(near, negativeWork, [peer], 12) == near, "超出阈值仍吸附");
    var scaled = NoteDocking.Snap(near, negativeWork, [peer], 18);
    Check(scaled.Left == -500 && scaled.Top == -200, "缩放或负坐标吸附错误");
    var free = new WindowBounds(-450, -140, 200, 160);
    Check(NoteDocking.Snap(free, negativeWork, [peer], 18) == free, "拖离后仍粘连");
});

Run("四边四角均可通过小幅连续拖动解除贴边", () =>
{
    var work = new WindowBounds(0, 0, 1920, 1040);
    foreach (var edges in new[] { DockEdges.Left, DockEdges.Right, DockEdges.Top, DockEdges.Bottom,
        DockEdges.Left | DockEdges.Top, DockEdges.Right | DockEdges.Top, DockEdges.Left | DockEdges.Bottom, DockEdges.Right | DockEdges.Bottom })
    {
        var initial = EdgeDocking.Align(new(500, 400, 324, 273), work, edges);
        var current = initial;
        var dx = edges.HasFlag(DockEdges.Left) ? 3 : edges.HasFlag(DockEdges.Right) ? -3 : 0;
        var dy = edges.HasFlag(DockEdges.Top) ? 3 : edges.HasFlag(DockEdges.Bottom) ? -3 : 0;
        for (var frame = 0; frame < 12; frame++)
        {
            var result = EdgeDocking.Snap(current with { Left = current.Left + dx, Top = current.Top + dy }, work, 12, previousBounds: current);
            Check(result.Edges == DockEdges.None, $"逐帧拖离仍被吸回：{edges}");
            current = result.Bounds;
        }
        Check(current.Left == initial.Left + dx * 12 && current.Top == initial.Top + dy * 12, "慢拖位移丢失");
    }
    var atLeft = new WindowBounds(0, 300, 324, 273);
    var outward = EdgeDocking.Snap(atLeft with { Left = -3 }, work, 12, previousBounds: atLeft);
    Check(outward.Bounds.Left == 0 && outward.Edges == DockEdges.Left, "向屏幕外推挤不应解除贴边");
});
Run("便签之间小幅连续拖离及沿边移动不会被吸回", () =>
{
    var work = new WindowBounds(0, 0, 1920, 1040);
    var peer = new WindowBounds(600, 350, 300, 220);
    foreach (var (initial, dx, dy) in new[]
    {
        (new WindowBounds(400, 350, 200, 160), -3, 0), (new WindowBounds(900, 350, 200, 160), 3, 0),
        (new WindowBounds(600, 190, 200, 160), 0, -3), (new WindowBounds(600, 570, 200, 160), 0, 3),
        (new WindowBounds(900, 350, 200, 160), 0, 3)
    })
    {
        var current = initial;
        for (var frame = 0; frame < 12; frame++)
            current = NoteDocking.Snap(current with { Left = current.Left + dx, Top = current.Top + dy }, work, [peer], 12, previousBounds: current);
        Check(current.Left == initial.Left + dx * 12 && current.Top == initial.Top + dy * 12, "便签被相接边或对齐边吸回");
    }
});

Run("便签之间贴边保留 2px 间距且慢拖可以脱离", () =>
{
    var work = new WindowBounds(2, 2, 1916, 1036);
    var peer = new WindowBounds(600, 350, 300, 220);
    foreach (var (near, expected) in new[]
    {
        (new WindowBounds(394, 355, 200, 160), new WindowBounds(398, 350, 200, 160)),
        (new WindowBounds(906, 355, 200, 160), new WindowBounds(902, 350, 200, 160)),
        (new WindowBounds(605, 184, 200, 160), new WindowBounds(600, 188, 200, 160)),
        (new WindowBounds(605, 576, 200, 160), new WindowBounds(600, 572, 200, 160))
    }) Check(NoteDocking.Snap(near, work, [peer], 12, gap: 2) == expected, "2px 间距或邻边对齐错误");
    var previous = new WindowBounds(902, 350, 200, 160);
    var away = previous with { Left = 905 };
    Check(NoteDocking.Snap(away, work, [peer], 12, previousBounds: previous, gap: 2).Left == 905, "2px 贴边后慢拖被吸回");
});

Console.WriteLine($"通过 {passed} 项数据集成检查。结果目录：{root}");
return 0;
void Run(string label, Action action)
{
    try { action(); passed++; Console.WriteLine($"通过：{label}"); }
    catch (Exception error) { Console.Error.WriteLine($"失败：{label}\n{error}"); Environment.Exit(1); }
}
DataStore Store(string name) => new(Path.Combine(root, name));
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static T Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T error) { return error; }
    throw new Exception($"预期 {typeof(T).Name}，操作却成功了。");
}
static AppData Example() => new()
{
    Theme = ThemePreference.Dark,
    Notes = [new() { Title = "工作 · 中文", Accent = AccentColor.Purple, Left = -300, Top = 124, Width = 420, Height = 500, IsVisible = false,
        Items = [new() { Text = "完成季度报告" }, new() { Text = "回复邮件\n补充附件", IsCompleted = true }] }]
};

