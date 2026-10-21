namespace ToolSuite.Installer;

public enum RegistryHiveKind { CurrentUser, LocalMachine }

/// <summary>卸载信息与套件自启值的注册表边界；检查可注入内存实现，不写真实注册表。</summary>
public interface IRegistryAccess
{
    string? ReadString(RegistryHiveKind hive, string subKey, string valueName);
    void WriteString(RegistryHiveKind hive, string subKey, string valueName, string value);
    void WriteDword(RegistryHiveKind hive, string subKey, string valueName, int value);
    void DeleteValue(RegistryHiveKind hive, string subKey, string valueName);
    void DeleteTree(RegistryHiveKind hive, string subKey);
}

/// <summary>开始菜单/桌面快捷方式边界；检查只记录调用，不创建真实快捷方式。</summary>
public interface IShortcutWriter
{
    void Create(string shortcutPath, string targetPath, string workingDirectory, string description);
    void Delete(string shortcutPath);
}

/// <summary>套件正在运行或文件被占用：明确要求退出后重试，不静默覆盖。</summary>
public sealed class InstallerBlockedException(string message, Exception? inner = null) : Exception(message, inner)
{
    public bool CanRetry => true;
}
