using System.Windows;
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
    [STAThread] public static int Main(string[] args)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["clipboard-check-sentinel"] = "host stays intact";
        var exit = 0;
        app.Startup += async (_,_) => {
            var clipboard = new FakeClipboard();
            var source = new FakeSource();
            var module = new ClipboardModule(new ClipboardPlatform(clipboard,clipboard,clipboard,clipboard,()=>source));
            try {
                var root=Path.GetFullPath(args[0]);
                var context=new ModuleContext(root,ThemePreference.Dark,Console.WriteLine);
                module.Configure(context);
                module.SetShortcut("Control+Shift+F23");
                Assert(app.Windows.Count==0,"offline shortcut must not start windows");
                Assert(new JsonStore(root).ReadSettings().Shortcut=="Control+Shift+F23","offline shortcut saved independently");
                await module.StartAsync(context,default);
                var panel=app.Windows.OfType<PanelWindow>().Single();
                Assert(panel.IsDocked,"first start is docked");
                Assert(Equals(app.Resources["clipboard-check-sentinel"],"host stays intact"),"module theme preserves host resources");
                clipboard.Text="isolated module record"; clipboard.Sequence++; source.Notify();
                await Task.Delay(100);
                Assert(((System.Windows.Controls.ListBox)panel.FindName("HistoryList")).Items.Count==1,"enabled capture updates real list");
                var combo=AccelCodec.Parse("Control+Alt+F22")!.Value;
                Assert(RegisterHotKey(IntPtr.Zero,0x6A78,(uint)combo.Modifiers,combo.VirtualKey),"test conflict reserved");
                try { module.SetShortcut("Control+Alt+F22"); throw new Exception("occupied shortcut should fail"); }
                catch(InvalidOperationException) { }
                finally { UnregisterHotKey(IntPtr.Zero,0x6A78); }
                Assert(new JsonStore(root).ReadSettings().Shortcut=="Control+Shift+F23","conflict preserves saved shortcut");
                PressToggle(); await Task.Delay(180);
                Assert(!panel.IsDocked,"real global shortcut summons");
                PressToggle(); await Task.Delay(180);
                Assert(panel.IsDocked,"same global shortcut docks");
                Directory.CreateDirectory(Path.Combine(root,"clipboard-history.json.tmp"));
                var refused=false;
                try { await module.PrepareToStopAsync(default); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { refused=true; }
                Assert(refused && app.Windows.OfType<PanelWindow>().Any(),"failed preflight preserves running window");
                Directory.Delete(Path.Combine(root,"clipboard-history.json.tmp"));
                await module.PrepareToStopAsync(default);
                await module.StopAsync(default);
                Assert(app.Windows.Count==0,"Stop releases all module windows");
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
            } catch(Exception ex) { Console.WriteLine(ex); exit=1; }
            finally { await module.StopAsync(default); app.Shutdown(); }
        };
        app.Run(); return exit;
    }
    static HistoryService Read(string root) { var h=new HistoryService(new HistoryStore(200,new ImageFileStore(root),()=>Guid.NewGuid().ToString(),()=>1),new JsonStore(root));h.LoadFromStorage();return h; }
    static void Assert(bool condition,string message) {if(!condition) throw new Exception(message);}
    static void PressToggle() { keybd_event(0x11,0,0,UIntPtr.Zero);keybd_event(0x10,0,0,UIntPtr.Zero);keybd_event(0x86,0,0,UIntPtr.Zero);keybd_event(0x86,0,2,UIntPtr.Zero);keybd_event(0x10,0,2,UIntPtr.Zero);keybd_event(0x11,0,2,UIntPtr.Zero); }
    [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
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
    public void Start(Action<bool> ready) => ready(true);
    public void Notify() => RoundHandler?.Invoke();
}

