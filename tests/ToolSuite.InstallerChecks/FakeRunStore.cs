using ToolSuite.Core;

namespace ToolSuite.InstallerChecks;

/// <summary>可控 Run 存储：记录每次访问，便于断言只动套件自己的值名。</summary>
internal sealed class FakeRunStore : IRunValueStore
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Writes { get; } = new();
    public List<string> Deletes { get; } = new();
    public bool FailWrites { get; set; }
    public bool FailDeletes { get; set; }
    /// <summary>写入已经落到存储后才报错，用于验证失败回滚不会留下“界面关闭但实际已注册”的残留。</summary>
    public bool FailAfterWrite { get; set; }
    public string? Read(string valueName) => Values.GetValueOrDefault(valueName);
    public void Write(string valueName, string command)
    {
        if (FailWrites) throw new UnauthorizedAccessException("拒绝访问模拟");
        Writes.Add(valueName);
        Values[valueName] = command;
        if (FailAfterWrite) throw new UnauthorizedAccessException("写入后报错模拟");
    }
    public void Delete(string valueName)
    {
        if (FailDeletes) throw new UnauthorizedAccessException("拒绝访问模拟");
        Deletes.Add(valueName);
        Values.Remove(valueName);
    }
}
