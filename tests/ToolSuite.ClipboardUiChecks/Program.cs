using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using ClipboardTool.Presentation.Wpf;
using ToolSuite.Core;
using ClipboardTool.Application;
using ClipboardTool.Domain.History;
using ClipboardTool.Domain.Hotkeys;
using ClipboardTool.Infrastructure.Windows;
internal static class Program
{
    private static System.Windows.Application _app = null!;

    [STAThread] public static int Main(string[] args)
    {
        _app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _app.Resources["clipboard-check-sentinel"] = "host stays intact";
        var exit = 0;
        _app.Startup += async (_,_) => {
            try {
                var root=Path.GetFullPath(args[0]);
                await TextScenario(Path.Combine(root,"text"));
                await ImageScenario(Path.Combine(root,"images"));
                await ThumbnailScenario(Path.Combine(root,"thumbs"));
                Console.WriteLine("PASS 剪贴板完整能力检查全部通过；系统剪贴板未被访问");
            } catch(Exception ex) { Console.WriteLine(ex); exit=1; }
            finally { _app.Shutdown(); }
        };
        _app.Run(); return exit;
    }

    /// <summary>文字链路（工单 02 基线）：真实列表、全局快捷键、冲突、离线设置、保存失败与重启。</summary>
    static async Task TextScenario(string root)
    {
        var clipboard = new FakeClipboard();
        var source = new FakeSource();
        var module = new ClipboardModule(new ClipboardPlatform(clipboard,clipboard,clipboard,clipboard,()=>source));
        try {
            var context=new ModuleContext(root,ThemePreference.Dark,Console.WriteLine);
            module.Configure(context);
            module.SetShortcut("Control+Shift+F23");
            Assert(_app.Windows.Count==0,"offline shortcut must not start windows");
            Assert(new JsonStore(root).ReadSettings().Shortcut=="Control+Shift+F23","offline shortcut saved independently");
            await module.StartAsync(context,default);
            var panel=_app.Windows.OfType<PanelWindow>().Single();
            Assert(panel.IsDocked,"first start is docked");
            Assert(Equals(_app.Resources["clipboard-check-sentinel"],"host stays intact"),"module theme preserves host resources");
            clipboard.Text="isolated module record"; clipboard.Sequence++; source.Notify();
            await Task.Delay(100);
            Assert(((System.Windows.Controls.ListBox)panel.FindName("HistoryList")).Items.Count==1,"enabled capture updates real list");
            var combo=AccelCodec.Parse("Control+Alt+F22")!.Value;
            Assert(RegisterHotKey(IntPtr.Zero,0x6A78,(uint)combo.Modifiers,combo.VirtualKey),"test conflict reserved");
            try { module.SetShortcut("Control+Alt+F22"); throw new Exception("occupied shortcut should fail"); }
            catch(InvalidOperationException) { }
            finally { UnregisterHotKey(IntPtr.Zero,0x6A78); }
            Assert(new JsonStore(root).ReadSettings().Shortcut=="Control+Shift+F23","conflict preserves saved shortcut");
            PressToggle(0x86); await Task.Delay(180);
            Assert(!panel.IsDocked,"real global shortcut summons");
            PressToggle(0x86); await Task.Delay(180);
            Assert(panel.IsDocked,"same global shortcut docks");
            Directory.CreateDirectory(Path.Combine(root,"clipboard-history.json.tmp"));
            var refused=false;
            try { await module.PrepareToStopAsync(default); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { refused=true; }
            Assert(refused && _app.Windows.OfType<PanelWindow>().Any(),"failed preflight preserves running window");
            Directory.Delete(Path.Combine(root,"clipboard-history.json.tmp"));
            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);
            Assert(_app.Windows.Count==0,"Stop releases all module windows");
            Assert(HotkeyAvailability.CanRegister(AccelCodec.Parse("Control+Shift+F23")!.Value),"Stop releases global shortcut");
            clipboard.Text="disabled should not capture"; clipboard.Sequence++; source.Notify();
            Assert(Read(root).Entries.Single().Text=="isolated module record","disabled does not capture and restart storage preserved");
            await module.StartAsync(context with { Theme=ThemePreference.Light },default);
            Assert(Read(root).Entries.Single().Text=="isolated module record","re-enable baseline skips clipboard while disabled");
            clipboard.Text="restarted record"; clipboard.Sequence++; source.Notify();
            await module.PrepareToStopAsync(default);
            await module.StopAsync(default);
            Assert(Read(root).Entries.Count==2,"re-enabled capture records once");
            Console.WriteLine("PASS real WPF list, global shortcut, conflict, offline setting, save failure, release and restart; system clipboard untouched");
        } finally { await module.StopAsync(default); }
    }

    /// <summary>图片/来源/搜索/置顶/备注/删除撤销/图片粘贴/清空历史，全部经真实窗口与模块命令。</summary>
    static async Task ImageScenario(string root)
    {
        var clipboard = new FakeClipboard();
        var source = new FakeSource();
        var module = new ClipboardModule(new ClipboardPlatform(clipboard,clipboard,clipboard,clipboard,()=>source));
        try {
            var context=new ModuleContext(root,ThemePreference.Dark,Console.WriteLine);
            module.Configure(context);
            module.SetShortcut("Control+Shift+F24");
            await module.StartAsync(context,default);
            var panel=_app.Windows.OfType<PanelWindow>().Single();
            var vm=(PanelViewModel)panel.DataContext;
            Assert(vm.Items.Count==0,"isolated root starts empty");

            // 主题：只替换本模块窗口的局部字典，宿主 Application 资源不动
            module.ApplyTheme(ThemePreference.Light);
            Assert(Equals(_app.Resources["clipboard-check-sentinel"],"host stays intact"),"theme switch keeps host resources");
            Assert(panel.Resources.MergedDictionaries.Count==1,"theme switch replaces only module-local dictionary");
            module.ApplyTheme(ThemePreference.Dark);

            // 图片记录：PNG 落盘、来源信息、图卡渲染
            var png=Png(0x20,0x80,0xE0);
            clipboard.Png=png; clipboard.Text=""; clipboard.Source=new SourceApp("C:\\Windows\\notepad.exe","Notepad","图片 - 记事本");
            clipboard.Sequence++; source.Notify();
            await WaitFor(()=>vm.Items.Count==1);
            var imageCard=vm.Items[0];
            Assert(imageCard.IsImage && imageCard.Body.Length==0,"image card replaces text body");
            Assert(imageCard.FileName==ImageFileNames.ForId(imageCard.Id),"image card shows stored file name");
            var imagePath=imageCard.ImagePath ?? throw new Exception("image card has no image path");
            Assert(File.Exists(imagePath),"image png persisted under module data dir");
            Assert(DibDecoder.DecodePngPixels(File.ReadAllBytes(imagePath)) is not null,"persisted png decodes");
            Assert(imageCard.Meta.Contains("Notepad"),"source app shown in meta line");

            // 文字记录：备注与来源参与后续搜索
            clipboard.Png=null; clipboard.Text="季度 报告 草稿"; clipboard.Source=new SourceApp("C:\\Windows\\notepad.exe","Notepad","report.txt - 记事本");
            clipboard.Sequence++; source.Notify();
            await WaitFor(()=>vm.Items.Count==2);
            var textCard=vm.Items.Single(card=>card.Body=="季度 报告 草稿");

            // 真实呼出键呼出后，默认方向键经全局键回到面板语义
            PressToggle(0x87); await WaitFor(()=>!panel.IsDocked);
            await WaitFor(()=>FindHosts(panel).Any(host=>host.EntryId==imageCard.Id && Source(host) is not null));
            PressKey(0x26); await Task.Delay(120);
            Assert(vm.SelectedIndex==0,"up at first item stays");
            PressKey(0x28); await WaitFor(()=>vm.SelectedIndex==1);
            PressKey(0x26); await WaitFor(()=>vm.SelectedIndex==0);

            // Z 置顶/取消置顶
            vm.SelectedIndex=vm.Items.ToList().FindIndex(card=>card.Id==textCard.Id);
            PressKey(0x5A);
            await WaitFor(()=>vm.Items[0].Id==textCard.Id && vm.Items[0].Pinned);
            PressKey(0x5A);
            await WaitFor(()=>vm.Items[0].Id==textCard.Id && !vm.Items[0].Pinned);

            // B 备注编辑：草稿经真实备注态保存并回显
            PressKey(0x42);
            await WaitFor(()=>vm.NoteEditingId==textCard.Id);
            vm.NoteDraft="  复核   待办  ";
            panel.HandlePanelKey("note-edit-exit",null);
            await WaitFor(()=>vm.Items.First(card=>card.Id==textCard.Id).Note=="复核   待办");
            Assert(vm.Toasts.Any(t=>t.Message=="备注已保存"),"note save reports result");

            // Space 进搜索；正文+备注、来源应用名+窗口标题；输入框走真实防抖链路
            PressKey(0x20);
            await WaitFor(()=>vm.SearchActive);
            var searchBox=(TextBox)panel.FindName("SearchBox");
            searchBox.Text="报告 复核";
            await WaitFor(()=>vm.Items.Count==1);
            Assert(vm.Items[0].Id==textCard.Id,"body+note multi-keyword search");
            Assert(vm.Items[0].SpanHits() && vm.Items[0].NoteSpans.Any(span=>span.Hit),"search highlights body and note");
            searchBox.Text="notepad report";
            await WaitFor(()=>vm.Items.Count==1);
            Assert(vm.Items[0].Id==textCard.Id,"source app name + window title search");
            searchBox.Text="不存在的词";
            await WaitFor(()=>vm.Items.Count==0);
            Assert(vm.EmptyText=="无匹配结果","search empty state distinguishes no match");
            // 搜索态 Esc 只退搜索，浏览态 Esc 停靠
            PressKey(0x1B);
            await WaitFor(()=>!vm.SearchActive && vm.Items.Count==2);
            Assert(!panel.IsDocked,"escape in search only leaves search mode");
            PressKey(0x1B);
            await WaitFor(()=>panel.IsDocked);
            PressToggle(0x87); await WaitFor(()=>!panel.IsDocked);

            // Del 删除：立即摘除 + 撤销恢复，撤销窗口内文件保留
            vm.SelectedIndex=vm.Items.ToList().FindIndex(card=>card.Id==imageCard.Id);
            PressKey(0x2E);
            await WaitFor(()=>vm.Items.All(card=>card.Id!=imageCard.Id));
            Assert(File.Exists(imageCard.ImagePath!),"pending deletion keeps png until deadline");
            var undo=vm.Toasts.LastOrDefault(t=>t.ActionLabel=="撤销") ?? throw new Exception("missing undo toast");
            undo.OnAction!();
            await WaitFor(()=>vm.Items.Any(card=>card.Id==imageCard.Id));

            // 收敛焦点快照（Esc 停靠已消费）后直接呼出几何：Enter 只走到写入剪贴板，
            // 不再向真实前台窗口注入按键（检查不接触真实剪贴板内容）
            PressKey(0x1B);
            await WaitFor(()=>panel.IsDocked);
            panel.Summon();
            await Pump(150);
            vm.SelectedIndex=vm.Items.ToList().FindIndex(card=>card.Id==imageCard.Id);
            panel.HandlePanelKey("enter",null);
            await WaitFor(()=>clipboard.ImageWrites.Count==1);
            Assert(clipboard.ImageWrites[0]==imageCard.ImagePath,"selected image written to clipboard");
            await WaitFor(()=>vm.FooterStatus.Length>0);
            Assert(vm.FooterStatus.Contains("无法恢复原输入框"),"missing focus target reports the paste failure");
            Assert(!panel.IsDocked,"failed paste keeps the panel available");
            await WaitFor(()=>vm.Items[0].Id==imageCard.Id);

            // 删除到期提交（真实 6 秒撤销窗口）：不撤销则落盘删除
            vm.SelectedIndex=vm.Items.ToList().FindIndex(card=>card.Id==textCard.Id);
            panel.HandlePanelKey("delete",null);
            await WaitFor(()=>vm.Items.All(card=>card.Id!=textCard.Id));
            await WaitFor(()=>Read(root).Entries.All(entry=>entry.Id!=textCard.Id),8000);
            Assert(vm.Toasts.All(t=>!t.Message.Contains("删除失败")),"deadline deletion reports no failure");

            // 清空历史：模块命令入口，删图片文件并给出结果反馈
            var clear=module.Commands.SingleOrDefault(command=>command.Id=="clear-history")
                ?? throw new Exception("clipboard module exposes no clear-history command");
            await clear.ExecuteAsync(default);
            await WaitFor(()=>vm.Items.Count==0);
            Assert(vm.Toasts.Any(t=>t.Message.Contains("清空")),"clear history reports its result");
            Assert(Directory.Exists(Path.Combine(root,"images")) && Directory.GetFiles(Path.Combine(root,"images")).Length==0,"clear history removes image files");
            var archive=new JsonStore(root).ReadHistory();
            Assert(archive is HistoryFileRead.Loaded { } loaded && loaded.Json.Trim()=="[]","cleared history persisted");
            Console.WriteLine("PASS 真实窗口图片记录/来源显示/多关键词搜索/置顶/备注/删除撤销/图片粘贴/清空历史与主题隔离");
        } finally { await module.StopAsync(default); }
        Assert(_app.Windows.Count==0,"clipboard scenario releases all windows");
    }

    /// <summary>缩略图：只对已实现（可视）项按显示尺寸解码、换绑不串图、停靠解除引用并作废在途结果。</summary>
    static async Task ThumbnailScenario(string root)
    {
        var imagesDir=Path.Combine(root,"images");
        Directory.CreateDirectory(imagesDir);
        var entries=new List<HistoryEntry>();
        for(var i=0;i<12;i++) {
            var id=$"img{i:00}";
            var path=Path.Combine(imagesDir,ImageFileNames.ForId(id));
            File.WriteAllBytes(path,Png((byte)(20*i),(byte)(200-10*i),(byte)(15*i)));
            entries.Add(new HistoryEntry(id,EntryKind.Image,null,path,1,null,false,0,string.Empty));
        }
        entries.Add(new HistoryEntry("text-card",EntryKind.Text,"文字卡片",null,2,null,false,0,string.Empty));

        var calls=new List<(string Id,int Size)>();
        var cache=new ThumbnailCache(
            (path,width)=>{ calls.Add((Path.GetFileNameWithoutExtension(path),width)); return WpfThumbnailDecoder.Decode(path,width); },
            work=>work(),
            action=>action());
        var window=new PanelWindow();
        try {
            window.SetThumbnailCache(cache);
            window.ReloadEntries(entries);
            window.Show();
            window.Summon();
            await Pump(400);

            var hosts=FindHosts(window);
            Assert(hosts.Count>0,"thumbnail hosts realized");
            Assert(hosts.Count<entries.Count,"only visible items are realized while rest stay virtual");
            var expectedSize=(int)Math.Round(ThumbnailHost.HeightDip*VisualTreeHelper.GetDpi(window).PixelsPerDip);
            Assert(calls.Count>0 && calls.All(call=>call.Size==expectedSize),$"decode uses display pixel size {expectedSize}");
            var realizedImages=hosts.Where(host=>host.EntryId!.StartsWith("img")).ToList();
            Assert(calls.Select(call=>call.Id).ToHashSet().SetEquals(realizedImages.Select(host=>host.EntryId!)),"decode only realized image cards");
            Assert(hosts.Where(host=>host.EntryId=="text-card").All(host=>Source(host) is null),"text card never decodes a thumbnail");
            foreach(var host in realizedImages) {
                var bitmap=Source(host) ?? throw new Exception($"realized image {host.EntryId} has no thumbnail");
                Assert(CenterColor(bitmap)==ExpectedColor(host.EntryId!),$"thumbnail for {host.EntryId} is not another entry's image");
            }

            // 换绑（重排）后不串图：每个宿主最终展示自己条目的像素
            window.ReloadEntries(entries.AsEnumerable().Reverse().ToList());
            await Pump(400);
            foreach(var host in FindHosts(window).Where(host=>host.EntryId!.StartsWith("img"))) {
                var bitmap=Source(host) ?? throw new Exception($"recycled image {host.EntryId} has no thumbnail");
                Assert(CenterColor(bitmap)==ExpectedColor(host.EntryId!),$"recycled container shows the wrong image for {host.EntryId}");
            }

            // 解码失败（文件丢失/坏图）：占位不缓存，不抛异常
            var missing=new HistoryEntry("img-missing",EntryKind.Image,null,Path.Combine(imagesDir,"missing.png"),4,null,false,0,string.Empty);
            window.ReloadEntries([missing,.. entries]);
            await Pump(300);
            var missingHost=FindHosts(window).FirstOrDefault(host=>host.EntryId=="img-missing");
            Assert(missingHost is null || Source(missingHost) is null,"decode failure leaves a placeholder");
            Assert(!cache.TryGet("img-missing",out _),"decode failure is not cached");

            // 条目消失（裁剪/清空/删除）时缓存按 id 差量失效
            var keptIds=entries.Where(entry=>entry.Id!="img00").ToList();
            window.ReloadEntries(keptIds);
            await Pump(200);
            Assert(!cache.TryGet("img00",out _),"removed entry leaves no cached thumbnail");
            Assert(cache.TryGet("img01",out _),"surviving entries keep their cached thumbnails");

            // 停靠：解除位图引用并清缓存
            window.Dock();
            await Pump(150);
            Assert(cache.Count==0,"docking clears thumbnail cache");
            Assert(FindHosts(window).All(host=>Source(host) is null),"docking releases thumbnail image references");

            // 停靠期间新增历史：内容不可见，不得解码缩略图
            calls.Clear();
            var extra=new HistoryEntry("img12",EntryKind.Image,null,entries[0].ImagePath,3,null,false,0,string.Empty);
            window.ReloadEntries([extra,.. entries]);
            await Pump(300);
            Assert(calls.Count==0,"docked panel decodes thumbnails for invisible items");
            Assert(cache.Count==0,"docked panel keeps no thumbnail cache entries");
            // 重新呼出：闸门重开，已实现容器补取（停靠期间的抑制不是永久的）
            window.Summon();
            await Pump(400);
            Assert(FindHosts(window).Where(host=>host.EntryId!.StartsWith("img")).All(host=>Source(host) is not null),
                "summoning resumes thumbnail loading for visible cards");
            window.Dock();
            await Pump(100);

            // 在途结果作废：停靠后完成的解码不得回填旧图
            var queued=new Queue<Action>();
            var lateCache=new ThumbnailCache(
                (path,width)=>WpfThumbnailDecoder.Decode(path,width),
                work=>queued.Enqueue(work),
                action=>action());
            window.SetThumbnailCache(lateCache);
            window.ReloadEntries(entries);
            window.Summon();
            await Pump(100);
            Assert(queued.Count>0,"thumbnail decode queued in background");
            window.Dock();
            while(queued.Count>0) queued.Dequeue()();
            await Pump(150);
            Assert(lateCache.Count==0,"in-flight decode after docking is discarded");
            Assert(FindHosts(window).All(host=>Source(host) is null),"late decode does not revive old bitmap");
            Console.WriteLine("PASS 缩略图只按可视项与显示尺寸解码、换绑不串图、停靠清缓存并使在途结果失效");
        } finally { window.Close(); }
    }

    static BitmapSource? Source(ThumbnailHost host) =>
        (VisualTreeHelper.GetChild(host,0) as Image)?.Source as BitmapSource;

    static List<ThumbnailHost> FindHosts(DependencyObject root)
    {
        var found=new List<ThumbnailHost>();
        var count=VisualTreeHelper.GetChildrenCount(root);
        for(var i=0;i<count;i++) {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is ThumbnailHost host) found.Add(host);
            else found.AddRange(FindHosts(child));
        }
        return found;
    }

    /// <summary>取缩略图中心像素（ARGB 打包），用于识别是否展示了别的条目。</summary>
    static uint CenterColor(BitmapSource bitmap)
    {
        var pixels=new byte[4];
        bitmap.CopyPixels(new Int32Rect(bitmap.PixelWidth/2,bitmap.PixelHeight/2,1,1),pixels,4,0);
        return (uint)(pixels[3]<<24|pixels[2]<<16|pixels[1]<<8|pixels[0]);
    }

    /// <summary>与生成文件时同一套颜色公式（独立真源：文件名里的序号）。</summary>
    static uint ExpectedColor(string id)
    {
        var index=int.Parse(id.AsSpan(3));
        return Argb((byte)(20*index),(byte)(200-10*index),(byte)(15*index));
    }

    static uint Argb(byte r,byte g,byte b)=>(uint)(0xFF<<24|r<<16|g<<8|b);

    static byte[] Png(byte r,byte g,byte b)
    {
        var rgba=new byte[4*4*4];
        for(var i=0;i<rgba.Length;i+=4) { rgba[i]=r; rgba[i+1]=g; rgba[i+2]=b; rgba[i+3]=0xFF; }
        return PngEncoder.Encode(4,4,rgba);
    }

    static async Task Pump(int ms)
    {
        var until=Environment.TickCount64+ms;
        while(Environment.TickCount64<until) await Task.Delay(20);
    }

    static async Task WaitFor(Func<bool> condition,int timeoutMs=4000)
    {
        var until=Environment.TickCount64+timeoutMs;
        while(Environment.TickCount64<until) {
            if(condition()) return;
            await Task.Delay(25);
        }
        if(!condition()) throw new Exception("check condition timed out");
    }

    static HistoryService Read(string root) { var h=new HistoryService(new HistoryStore(200,new ImageFileStore(root),()=>Guid.NewGuid().ToString(),()=>1),new JsonStore(root));h.LoadFromStorage();return h; }
    static void Assert(bool condition,string message) {if(!condition) throw new Exception(message);}
    static void PressKey(byte key) { keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,2,UIntPtr.Zero); }
    static void PressToggle(byte key) { keybd_event(0x11,0,0,UIntPtr.Zero);keybd_event(0x10,0,0,UIntPtr.Zero);keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,2,UIntPtr.Zero);keybd_event(0x10,0,2,UIntPtr.Zero);keybd_event(0x11,0,2,UIntPtr.Zero); }
    [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
}

static class CardCheckExtensions
{
    public static bool SpanHits(this CardViewModel card)=>card.Spans.Any(span=>span.Hit);
}

sealed class FakeClipboard : IClipboardReader,IClipboardWriter,IClipboardSequence,IForegroundSource
{
    public string Text = ""; public byte[]? Png; public uint Sequence = 1;
    public SourceApp? Source { get; set; }
    public List<string> ImageWrites { get; } = [];
    public ClipboardReadOutcome Read() => new ClipboardReadOutcome.Known(new ClipboardSnapshot(Text,Png));
    public uint Current() => Sequence;
    public bool WriteText(string text) { Text=text; Png=null; Sequence++; return true; }
    public bool WriteImage(string path) { ImageWrites.Add(path); Png=File.ReadAllBytes(path); Text=""; Sequence++; return true; }
    public SourceApp? Capture()=>Source;
}
sealed class FakeSource : IClipboardChangeEventSource
{
    public Func<bool>? RoundHandler {get;set;}
    public void Start(Action<bool> ready) => ready(true);
    public void Notify() => RoundHandler?.Invoke();
}
