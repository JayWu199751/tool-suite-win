namespace ToolSuite.Core;

/// <summary>自启命令的存储边界；生产实现使用登录计划任务，旧 Run 存储仅用于迁移。</summary>
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

public sealed class SuiteAutoStart(IRunValueStore store, string executablePath,
    IRunValueStore? legacyStore = null) : ISuiteAutoStart
{
    public const string ValueName = SuiteIdentity.RunValueName;

    private readonly string command = "\"" + (string.IsNullOrWhiteSpace(executablePath)
        ? throw new ArgumentException("开机启动必须指向正式 EXE。", nameof(executablePath))
        : Path.GetFullPath(executablePath)) + "\"";

    public bool IsRegistered
    {
        get
        {
            // 只有旧版明确开启过自启时才迁移；首次使用仍默认关闭。
            if (legacyStore?.Read(ValueName) is not null) Apply(true);
            return string.Equals(store.Read(ValueName), command, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>只写/删套件自己的值；失败原样抛出可读原因，由设置界面显示且不记为生效。</summary>
    public void Apply(bool enabled)
    {
        string? previous = null;
        string? legacy = null;
        var captured = false;
        try
        {
            previous = store.Read(ValueName);
            legacy = legacyStore?.Read(ValueName);
            captured = true;
            if (enabled) store.Write(ValueName, command);
            else store.Delete(ValueName);
            if (legacy is not null) legacyStore!.Delete(ValueName);
        }
        catch (Exception ex)
        {
            if (captured)
            {
                try
                {
                    if (previous is null) store.Delete(ValueName);
                    else store.Write(ValueName, previous);
                    if (legacy is not null) legacyStore!.Write(ValueName, legacy);
                }
                catch { /* 保留原始注册错误，界面不得报告成功。 */ }
            }
            throw new InvalidOperationException(
                $"无法{(enabled ? "注册" : "注销")}开机启动：{ex.Message}", ex);
        }
    }
}
