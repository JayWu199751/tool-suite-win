namespace ToolSuite.Core;

/// <summary>开机启动的存储边界。生产实现只允许读写 HKCU Run 下套件自己的值名。</summary>
public interface IRunValueStore
{
    string? Read(string valueName);
    void Write(string valueName, string command);
    void Delete(string valueName);
}

/// <summary>宿主使用的统一开机启动入口；检查可注入可控存储，不接触真实注册表。</summary>
public interface ISuiteAutoStart
{
    bool IsRegistered { get; }
    void Apply(bool enabled);
}

public sealed class SuiteAutoStart(IRunValueStore store, string executablePath) : ISuiteAutoStart
{
    public const string ValueName = SuiteIdentity.RunValueName;

    private readonly string command = "\"" + (string.IsNullOrWhiteSpace(executablePath)
        ? throw new ArgumentException("开机启动必须指向正式 EXE。", nameof(executablePath))
        : Path.GetFullPath(executablePath)) + "\"";

    public bool IsRegistered => store.Read(ValueName) is not null;

    /// <summary>只写/删套件自己的值；失败原样抛出可读原因，由设置界面显示且不记为生效。</summary>
    public void Apply(bool enabled)
    {
        try
        {
            if (enabled) store.Write(ValueName, command);
            else if (IsRegistered) store.Delete(ValueName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法{(enabled ? "注册" : "注销")}开机启动：{ex.Message}", ex);
        }
    }
}
