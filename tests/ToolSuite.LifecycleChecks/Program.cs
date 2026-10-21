using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardTool.Application;
using ClipboardTool.Domain.History;
using ClipboardTool.Infrastructure.Windows;
using ClipboardTool.Presentation.Wpf;
using PerfMonitor.App;
using PerfMonitor.Core.Settings;
using PerfMonitor.Windows.Settings;
using Pinto;
using Pinto.Core;
using Pinto.Views;
using ToolSuite.Core;
using SuiteTheme = ToolSuite.Core.ThemePreference;

internal static class Program
{
    private static Application _app = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "artifacts", "lifecycle")),
            "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
        _app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exit = 0;
        _app.Startup += async (_, _) =>
        {
            try
            {
                await PintoScenario(Path.Combine(root, "pinto"));
                await PerformanceBarScenario(Path.Combine(root, "perf"), Path.Combine(root, "perf-time"));
                await ClipboardThumbnailScenario(Path.Combine(root, "thumbnails"));
                await SuiteExitScenario(Path.Combine(root, "suite"));
                Console.WriteLine("PASS 隐藏界面回收、重建与退出检查全部通过");
            }
            catch (Exception error) { Console.WriteLine(error); exit = 1; }
            finally { _app.Shutdown(); }
        };
        _app.Run();
        return exit;
    }

    /// <summary>便签：启动不为隐藏项建窗，隐藏保存后释放，重建恢复内容/完成/布局/贴边；保存失败保留可恢复编辑。</summary>
    private static async Task PintoScenario(string root)
    {
        var store = new DataStore(root);
        var hidden = new NoteData
        {
            Title = "隐藏事项",
            IsVisible = false,
            Left = 120,
            Top = 140,
            Width = 360,
            Height = 300,
            Accent = AccentColor.Green,
            Items =
            [
                new ChecklistItemData { Text = "已完成项", IsCompleted = true },
                new ChecklistItemData { Text = "未完成项" }
            ]
        };
        var docked = new NoteData { Title = "贴边事项", IsVisible = false, DockEdges = DockEdges.Left | DockEdges.Top };
        store.Save(new AppData { Notes = [hidden, docked] });

        IToolModule module = new PintoModule();
        try
        {
            await module.StartAsync(new ModuleContext(root, SuiteTheme.Light, Console.WriteLine), default);
            Check("启动不为隐藏便签创建窗口", !_app.Windows.OfType<NoteWindow>().Any());

            await ShowNote(module, hidden.Id);
            var window = _app.Windows.OfType<NoteWindow>().Single();
            Check("显示隐藏便签时按需创建窗口并恢复内容与完成状态",
                window.IsVisible && window.Model.Title == "隐藏事项" && window.Model.Items.Count == 2
                && window.Model.Items[0].Text == "已完成项" && window.Model.Items[0].IsCompleted
                && window.Model.Items[1].Text == "未完成项" && !window.Model.Items[1].IsCompleted);
            Check("重建后恢复位置与尺寸",
                Math.Abs(window.Left - 120) < 1 && Math.Abs(window.Top - 140) < 1
                && Math.Abs(window.Width - 360) < 1 && Math.Abs(window.Height - 300) < 1);

            await HideAll(module);
            Check("隐藏保存成功后释放窗口", !_app.Windows.OfType<NoteWindow>().Any());

            await ShowNote(module, hidden.Id);
            window = _app.Windows.OfType<NoteWindow>().Single();
            Check("再次打开恢复内容、完成状态与布局",
                window.Model.Items.Count == 2 && window.Model.Items[0].IsCompleted
                && Math.Abs(window.Left - 120) < 1 && Math.Abs(window.Top - 140) < 1
                && Math.Abs(window.Width - 360) < 1 && Math.Abs(window.Height - 300) < 1);

            var editor = (TextBox)window.FindName("NewItemEditor");
            editor.Text = "隐藏失败时保留的草稿";
            using (new FileStream(store.StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                window.Close();
                await Task.Delay(100);
                var kept = _app.Windows.OfType<NoteWindow>().SingleOrDefault();
                Check("保存失败保留窗口与可恢复编辑",
                    kept is not null && kept.IsVisible && ReferenceEquals(kept, window)
                    && kept.Model.Items.Any(item => item.Text == "隐藏失败时保留的草稿"));
            }

            window.Close();
            await Task.Delay(100);
            Check("故障解除后可继续隐藏并释放窗口", !_app.Windows.OfType<NoteWindow>().Any());
            await ShowNote(module, hidden.Id);
            window = _app.Windows.OfType<NoteWindow>().Single();
            Check("重建保留故障期间提交的编辑且不重复",
                window.Model.Items.Count == 3 && window.Model.Items.Count(item => item.Text == "隐藏失败时保留的草稿") == 1);

            for (var round = 1; round <= 3; round++)
            {
                await HideAll(module);
                await ShowNote(module, hidden.Id);
                window = _app.Windows.OfType<NoteWindow>().Single();
                Check($"第 {round} 次显示/隐藏后只有一个窗口且内容不重复",
                    window.IsVisible && window.Model.Items.Count == 3);
            }

            await HideAll(module);
            await ShowNote(module, docked.Id);
            var dockedWindow = _app.Windows.OfType<NoteWindow>().Single();
            var work = SystemParameters.WorkArea;
            Check("贴边便签重建后仍贴左/上边",
                Math.Abs(dockedWindow.Left - work.Left) < 8 && Math.Abs(dockedWindow.Top - work.Top) < 8);
            await HideAll(module);
            await ShowNote(module, docked.Id);
            var rebuiltDocked = _app.Windows.OfType<NoteWindow>().Single();
            Check("重复重建后贴边仍生效",
                Math.Abs(rebuiltDocked.Left - work.Left) < 8 && Math.Abs(rebuiltDocked.Top - work.Top) < 8);

            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);
            Check("便签停用释放全部窗口", !_app.Windows.OfType<NoteWindow>().Any());
            Check("便签停用释放数据文件锁", CanOpenExclusive(store.StatePath));

            // 导入对话框按需创建、关闭释放；重建显示当前备份与现有数量。
            var dialog = new ImportDialog(3, 2);
            dialog.Show();
            await Task.Delay(60);
            Check("便签导入对话框按需创建并显示数量",
                _app.Windows.OfType<ImportDialog>().Count() == 1
                && ((TextBlock)dialog.FindName("Summary")).Text.Contains("3 张")
                && ((TextBlock)dialog.FindName("Summary")).Text.Contains("2 张"));
            dialog.Close();
            await Task.Delay(60);
            Check("便签导入对话框关闭后释放", !_app.Windows.OfType<ImportDialog>().Any());
            var rebuiltDialog = new ImportDialog(1, 0);
            rebuiltDialog.Show();
            await Task.Delay(60);
            Check("重建导入对话框显示新的数量",
                !ReferenceEquals(dialog, rebuiltDialog)
                && ((TextBlock)rebuiltDialog.FindName("Summary")).Text.Contains("1 张"));
            rebuiltDialog.Close();
            await Task.Delay(60);

            await module.StartAsync(new ModuleContext(root, SuiteTheme.Light, Console.WriteLine), default);
            Check("重新启用只为可见便签建窗，隐藏便签仍无窗口",
                _app.Windows.OfType<NoteWindow>().Count() == 1
                && _app.Windows.OfType<NoteWindow>().Single().Model.Title == "贴边事项"
                && _app.Windows.OfType<NoteWindow>().All(window => window.Model.Title != "隐藏事项"));
            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);
        }
        finally { await module.StopAsync(default); }
        Console.WriteLine("PASS 便签按需建窗、隐藏回收、失败保留与重建");
    }

    /// <summary>性能条：设置窗按需创建关闭释放、重建保留配置；隐藏暂停时钟；停用释放窗口、重启仍单实例。</summary>
    private static async Task PerformanceBarScenario(string settingsRoot, string clockRoot)
    {
        Directory.CreateDirectory(settingsRoot);
        var settingsPath = Path.Combine(settingsRoot, "settings.json");
        new WindowsSettingsStore(settingsPath).Save(PerformanceSettings.Default with
        {
            Metrics = new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false, Time = false }
        });
        var module = new PerfMonitorModule();
        try
        {
            await module.StartAsync(new ModuleContext(settingsRoot, SuiteTheme.Dark, Console.WriteLine), default);
            Check("性能条启动只有一个窗口", _app.Windows.OfType<PerformanceBarWindow>().Count() == 1);

            await module.Commands.Single(command => command.Id == "settings").ExecuteAsync(default);
            var first = _app.Windows.OfType<SettingsWindow>().Single();
            var gpu = (ToggleButton)first.FindName("GpuToggle");
            gpu.IsChecked = false;
            first.Close();
            await Task.Delay(60);
            Check("性能条设置关闭后释放窗口", !_app.Windows.OfType<SettingsWindow>().Any());

            await module.Commands.Single(command => command.Id == "settings").ExecuteAsync(default);
            var rebuilt = _app.Windows.OfType<SettingsWindow>().Single();
            Check("重建设置窗显示已保存配置并可继续操作",
                !ReferenceEquals(first, rebuilt) && ((ToggleButton)rebuilt.FindName("GpuToggle")).IsChecked == false
                && new WindowsSettingsStore(settingsPath).Load().Metrics.Gpu == false);
            ((ToggleButton)rebuilt.FindName("GpuToggle")).IsChecked = true;
            rebuilt.Close();
            await Task.Delay(60);
            Check("重建设置窗的修改继续生效", new WindowsSettingsStore(settingsPath).Load().Metrics.Gpu);

            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);
            Check("停用性能条释放窗口与文件锁",
                !_app.Windows.OfType<PerformanceBarWindow>().Any() && !_app.Windows.OfType<SettingsWindow>().Any()
                && CanOpenExclusive(settingsPath));

            await module.StartAsync(new ModuleContext(settingsRoot, SuiteTheme.Light, Console.WriteLine), default);
            Check("重新启用只重建一个性能条窗口", _app.Windows.OfType<PerformanceBarWindow>().Count() == 1);
            await module.Commands.Single(command => command.Id == "visibility").ExecuteAsync(default);
            Check("隐藏后窗口仍在但不显示", _app.Windows.OfType<PerformanceBarWindow>().Single().IsVisible == false);
            await module.Commands.Single(command => command.Id == "visibility").ExecuteAsync(default);
            Check("再次显示后只有一个可见性能条", _app.Windows.OfType<PerformanceBarWindow>().Count() == 1
                && _app.Windows.OfType<PerformanceBarWindow>().Single().IsVisible);
            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);

            // 时钟只随可见性更新：隐藏期间不产生新的时钟文本。
            Directory.CreateDirectory(clockRoot);
            new WindowsSettingsStore(Path.Combine(clockRoot, "settings.json")).Save(PerformanceSettings.Default with
            {
                Metrics = new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false, Time = true }
            });
            await module.StartAsync(new ModuleContext(clockRoot, SuiteTheme.Light, Console.WriteLine), default);
            var bar = _app.Windows.OfType<PerformanceBarWindow>().Single();
            var viewModel = (PerformanceBarViewModel)bar.DataContext;
            var before = viewModel.TimeText;
            Check("可见时时钟持续更新", await ChangesWithin(() => viewModel.TimeText, before));
            await module.Commands.Single(command => command.Id == "visibility").ExecuteAsync(default);
            var hiddenAt = viewModel.TimeText;
            await Task.Delay(1300);
            Check("不可见时不更新时钟", viewModel.TimeText == hiddenAt);
            await module.Commands.Single(command => command.Id == "visibility").ExecuteAsync(default);
            var shownAt = viewModel.TimeText;
            Check("重新显示后时钟恢复更新", await ChangesWithin(() => viewModel.TimeText, shownAt));
            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);
        }
        finally { await module.StopAsync(default); }
        Console.WriteLine("PASS 性能条设置窗按需创建、停用释放与时钟暂停");
    }

    /// <summary>剪贴板：面板收起释放缩略图引用与缓存，在途解码作废不回写，呼出后重新加载。</summary>
    private static async Task ClipboardThumbnailScenario(string root)
    {
        var images = Path.Combine(root, "images");
        Directory.CreateDirectory(images);
        var entries = new List<HistoryEntry>();
        for (var i = 0; i < 4; i++)
        {
            var id = $"img{i:00}";
            var path = Path.Combine(images, ImageFileNames.ForId(id));
            File.WriteAllBytes(path, Png((byte)(30 * i), (byte)(200 - 20 * i), (byte)(10 * i)));
            entries.Add(new HistoryEntry(id, EntryKind.Image, null, path, i + 1, null, false, 0, string.Empty));
        }

        var queued = new Queue<Action>();
        var cache = new ThumbnailCache(
            (path, width) => WpfThumbnailDecoder.Decode(path, width),
            work => queued.Enqueue(work),
            action => action());
        var window = new PanelWindow();
        try
        {
            window.SetThumbnailCache(cache);
            window.ReloadEntries(entries);
            window.Show();
            window.Summon();
            await RunQueued(queued);
            Check("呼出时为可视图片卡加载缩略图", cache.Count > 0 && FindHosts(window).Any(host => Bitmap(host) is not null));

            window.Dock();
            Check("面板收起解除缩略图缓存与图片引用",
                cache.Count == 0 && FindHosts(window).All(host => Bitmap(host) is null));

            window.Summon();
            await Task.Delay(120);
            window.Dock();
            while (queued.Count > 0) queued.Dequeue()();
            await Task.Delay(80);
            Check("收起后完成的在途解码不回写旧窗口",
                cache.Count == 0 && FindHosts(window).All(host => Bitmap(host) is null));

            window.Summon();
            await RunQueued(queued);
            Check("再次呼出仍能正常加载缩略图",
                FindHosts(window).Where(host => host.EntryId?.StartsWith("img") == true).All(host => Bitmap(host) is not null));
            window.Dock();
        }
        finally { window.Close(); }
        Console.WriteLine("PASS 剪贴板收起释放缩略图、在途结果作废且呼出正常");
    }

    /// <summary>整套退出：三个真实模块同时停用后释放窗口与文件锁，重复退出/启动正常。</summary>
    private static async Task SuiteExitScenario(string root)
    {
        var store = new SuiteSettingsStore(root);
        Directory.CreateDirectory(store.ModuleDirectory("perf-monitor"));
        new WindowsSettingsStore(Path.Combine(store.ModuleDirectory("perf-monitor"), "settings.json")).Save(
            PerformanceSettings.Default with
            {
                Metrics = new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false, Time = false }
            });
        var clipboard = new FakeClipboard();
        var source = new FakeSource();
        var controller = new SuiteController(store,
        [
            new PintoModule(),
            new PerfMonitorModule(),
            new ClipboardModule(new ClipboardPlatform(clipboard, clipboard, clipboard, clipboard, () => source))
        ]);
        try
        {
            await controller.StartAsync();
            Check("整套启动接入三个真实模块",
                _app.Windows.OfType<NoteWindow>().Count() == 1
                && _app.Windows.OfType<PerformanceBarWindow>().Count() == 1
                && _app.Windows.OfType<PanelWindow>().Count() == 1);
            Check("整套退出预检保存成功", await controller.PrepareExitAsync());
            await controller.StopAsync();
            Check("整套退出释放所有模块窗口", !HasModuleWindow());

            var stateFiles = new[]
            {
                new DataStore(store.ModuleDirectory("pinto")).StatePath,
                Path.Combine(store.ModuleDirectory("perf-monitor"), "settings.json"),
                Path.Combine(store.ModuleDirectory("clipboard"), "settings.json"),
                Path.Combine(store.ModuleDirectory("clipboard"), "clipboard-history.json")
            };
            Check("整套退出释放数据文件锁", stateFiles.All(CanOpenExclusive));

            await controller.StartAsync();
            Check("整套可重复启动且窗口不重复",
                _app.Windows.OfType<NoteWindow>().Count() == 1
                && _app.Windows.OfType<PerformanceBarWindow>().Count() == 1
                && _app.Windows.OfType<PanelWindow>().Count() == 1);
            await controller.StopAsync();
            Check("整套可重复退出", !HasModuleWindow());
        }
        finally { await controller.StopAsync(); }
        Console.WriteLine("PASS 整套退出释放窗口与文件锁，可重复启停");
    }

    private static async Task ShowNote(IToolModule module, Guid id) =>
        await module.Commands.Single(command => command.Id == $"show-note/{id}").ExecuteAsync(default);

    private static async Task HideAll(IToolModule module) =>
        await module.Commands.Single(command => command.Id == "hide-all").ExecuteAsync(default);

    private static bool HasModuleWindow() =>
        _app.Windows.OfType<NoteWindow>().Any()
        || _app.Windows.OfType<PerformanceBarWindow>().Any()
        || _app.Windows.OfType<SettingsWindow>().Any()
        || _app.Windows.OfType<PanelWindow>().Any();

    private static bool CanOpenExclusive(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
    }

    private static async Task<bool> ChangesWithin(Func<string> read, string previous)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < 2500)
        {
            await Task.Delay(100);
            if (read() != previous) return true;
        }
        return false;
    }

    private static async Task RunQueued(Queue<Action> queued)
    {
        await Task.Delay(120);
        while (queued.Count > 0) queued.Dequeue()();
        await Task.Delay(120);
    }

    private static BitmapSource? Bitmap(ThumbnailHost host) =>
        (VisualTreeHelper.GetChild(host, 0) as Image)?.Source as BitmapSource;

    private static List<ThumbnailHost> FindHosts(DependencyObject root)
    {
        var found = new List<ThumbnailHost>();
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ThumbnailHost host) found.Add(host);
            else found.AddRange(FindHosts(child));
        }
        return found;
    }

    private static byte[] Png(byte r, byte g, byte b)
    {
        var rgba = new byte[4 * 4 * 4];
        for (var i = 0; i < rgba.Length; i += 4) { rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = 0xFF; }
        return PngEncoder.Encode(4, 4, rgba);
    }

    private static void Check(string message, bool condition)
    {
        if (!condition) throw new Exception("失败：" + message);
        Console.WriteLine("通过：" + message);
    }
}

internal sealed class FakeClipboard : IClipboardReader, IClipboardWriter, IClipboardSequence, IForegroundSource
{
    public string Text = "";
    public uint Sequence = 1;
    public ClipboardReadOutcome Read() => new ClipboardReadOutcome.Known(new ClipboardSnapshot(Text, null));
    public uint Current() => Sequence;
    public bool WriteText(string text) { Text = text; Sequence++; return true; }
    public bool WriteImage(string path) => false;
    public SourceApp? Capture() => null;
}

internal sealed class FakeSource : IClipboardChangeEventSource
{
    public Func<bool>? RoundHandler { get; set; }
    private Action<bool>? _ready;
    public void Start(Action<bool> ready) => _ready = ready;
    public void ReportReady(bool success) => _ready?.Invoke(success);
}
