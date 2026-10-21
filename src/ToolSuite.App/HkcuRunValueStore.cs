using Microsoft.Win32;
using ToolSuite.Core;

namespace ToolSuite.App;

/// <summary>真实的 HKCU Run 适配器：只访问 Run 键与套件自己的值名，不枚举或修改其他应用启动项。</summary>
internal sealed class HkcuRunValueStore : IRunValueStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(valueName) as string;
    }

    public void Write(string valueName, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("无法打开开机启动注册表项。");
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void Delete(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
