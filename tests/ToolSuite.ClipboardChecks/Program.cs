using ClipboardTool.Application;
using ClipboardTool.Domain.History;
using ClipboardTool.Infrastructure.Windows;
var root = args[0];
Directory.CreateDirectory(root);
var json = new JsonStore(root);
HistoryService Open() { var h = new HistoryService(new HistoryStore(200,new ImageFileStore(root),()=>Guid.NewGuid().ToString(),()=>1),json); h.LoadFromStorage(); return h; }
var history = Open();
history.RecordText("套件文字记录");
Directory.CreateDirectory(Path.Combine(root,"clipboard-history.json.tmp"));
try { history.SaveCurrent(); throw new Exception("保存拒绝时不得成功"); } catch(Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
Directory.Delete(Path.Combine(root,"clipboard-history.json.tmp"));
history.SaveCurrent();
if(Open().Entries.Single().Text != "套件文字记录") throw new Exception("重启恢复文字失败");
Console.WriteLine("PASS 保存失败可观察、重试成功与独立数据重启恢复");


var reader = new FakeClipboard();
var recording = Open();
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
