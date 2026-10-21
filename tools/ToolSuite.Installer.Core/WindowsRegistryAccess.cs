using Microsoft.Win32;

namespace ToolSuite.Installer;

public sealed class WindowsRegistryAccess : IRegistryAccess
{
    public string? ReadString(RegistryHiveKind hive, string subKey, string valueName)
    {
        using var key = Root(hive).OpenSubKey(subKey);
        return key?.GetValue(valueName) as string;
    }

    public void WriteString(RegistryHiveKind hive, string subKey, string valueName, string value)
    {
        using var key = Root(hive).CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException("无法打开注册表项：" + subKey);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void WriteDword(RegistryHiveKind hive, string subKey, string valueName, int value)
    {
        using var key = Root(hive).CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException("无法打开注册表项：" + subKey);
        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName)
    {
        using var key = Root(hive).OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteTree(RegistryHiveKind hive, string subKey)
    {
        var separator = subKey.LastIndexOf('\\');
        var parentPath = separator > 0 ? subKey[..separator] : null;
        var leaf = subKey[(separator + 1)..];
        if (parentPath is null)
        {
            Root(hive).DeleteSubKeyTree(leaf, throwOnMissingSubKey: false);
            return;
        }
        using var parent = Root(hive).OpenSubKey(parentPath, writable: true);
        parent?.DeleteSubKeyTree(leaf, throwOnMissingSubKey: false);
    }

    private static RegistryKey Root(RegistryHiveKind hive) =>
        hive == RegistryHiveKind.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
}
