using System.IO.Compression;
using ToolSuite.Core;

namespace ToolSuite.Installer;

public sealed record InstallResult(string InstallDirectory, string Version, bool Upgraded);

/// <summary>套件自身的安装、升级与卸载。只操作给定安装目录、套件自己的登录任务、旧 Run 值与卸载键；
/// 从不读取、迁移或修改原三个应用的数据、安装与启动项，也不触碰 %LOCALAPPDATA%/ToolSuite 用户数据。</summary>
public sealed class InstallerEngine(IRegistryAccess registry, IShortcutWriter shortcuts,
    string? setupSourcePath = null, string? version = null, IRunValueStore? startupTasks = null)
{
    private readonly string version = version ?? SuiteIdentity.Version;
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + SuiteIdentity.UninstallKeyName;

    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", SuiteIdentity.DataFolderName);

    public static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), SuiteIdentity.ProductName + ".lnk");

    public static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), SuiteIdentity.ProductName + ".lnk");

    /// <summary>安装或升级：先确认套件未运行，再整体替换安装目录；失败不覆盖旧文件。
    /// 替换目录、写卸载登记与写快捷方式属于同一次事务：任一步失败都回到替换前状态，
    /// 不留下“用户被告知失败、旧安装却已删除”的空档。</summary>
    public InstallResult Install(Func<Stream> openPayload, string installDirectory, bool startMenu, bool desktop)
    {
        var install = Normalize(installDirectory);
        var exePath = Path.Combine(install, SuiteIdentity.ExecutableName);
        EnsureNotRunning(exePath);
        EnsureNotRunning(Path.Combine(install, SuiteIdentity.SetupExecutableName));
        var upgraded = File.Exists(exePath);
        // 回滚要把卸载登记恢复成旧版本号：替换前先记住旧安装的版本，事后再从磁盘兜底。
        var previousVersion = upgraded ? ReadInstalledVersion(install) : null;

        var staging = install + ".staging-" + Guid.NewGuid().ToString("N");
        var previous = install + ".previous-" + Guid.NewGuid().ToString("N");
        var movedPrevious = false;
        var installed = false;
        var completed = false;
        try
        {
            Directory.CreateDirectory(staging);
            Extract(openPayload(), staging);
            if (!File.Exists(Path.Combine(staging, SuiteIdentity.ExecutableName)))
                throw new InvalidDataException("安装包内容缺少 " + SuiteIdentity.ExecutableName + "。");
            if (!string.IsNullOrWhiteSpace(setupSourcePath) && File.Exists(setupSourcePath))
                File.Copy(setupSourcePath, Path.Combine(staging, SuiteIdentity.SetupExecutableName), overwrite: true);
            File.WriteAllText(Path.Combine(staging, "version.txt"), version);

            if (Directory.Exists(install))
            {
                // 文件占用已在上面确认；先移到旁边，替换失败时仍可恢复旧安装。
                Directory.Move(install, previous);
                movedPrevious = true;
            }
            Directory.Move(staging, install);
            installed = true;

            WriteUninstallRegistration(install);
            ApplyShortcuts(install, startMenu, desktop);
            completed = true;
        }
        catch (Exception exception)
        {
            if (!installed)
            {
                if (movedPrevious && !Directory.Exists(install))
                {
                    try { Directory.Move(previous, install); } catch { /* 原始安装错误优先。 */ }
                }
                throw;
            }

            // 替换已经发生：先清掉可能半写的登记与快捷方式，再恢复旧安装。
            // 文件恢复失败才算回滚未完成；登记/快捷方式还原失败按可恢复的次生失败上报。
            var (essential, restored) = RollBack(install, previous, movedPrevious, previousVersion, startMenu, desktop);
            if (essential.Count == 0 && restored.Count == 0) throw;
            if (essential.Count == 0)
                throw new InvalidOperationException(
                    $"{exception.Message} 旧安装已恢复，但卸载登记或快捷方式未能还原：{string.Join("；", restored)}。", exception);
            var residual = Directory.Exists(previous) ? previous : install;
            var detail = string.Join("；", essential.Concat(restored));
            throw new InvalidOperationException(
                $"{exception.Message} 安装回滚未完成：{detail}。残留位置：{residual}。", exception);
        }
        finally
        {
            if (Directory.Exists(staging)) TryDeleteDirectory(staging);
            // 旧目录只在新安装与登记全部落地后才丢弃；失败时它是用户唯一的旧安装或回滚现场。
            if (completed && Directory.Exists(previous)) TryDeleteDirectory(previous);
        }

        return new InstallResult(install, version, upgraded);
    }

    /// <summary>回滚到替换前状态：删半写登记与快捷方式，升级场景恢复旧目录并把登记与快捷方式
    /// 还原成旧安装，全新安装删除新目录。返回（文件恢复失败，登记/快捷方式还原失败）；
    /// 每一步独立记录，不抛出，避免掩盖原始安装异常。</summary>
    private (List<string> Essential, List<string> Restored) RollBack(string install, string previous, bool hadPrevious,
        string? previousVersion, bool startMenu, bool desktop)
    {
        List<string> essential = [];
        List<string> restored = [];
        try { registry.DeleteTree(RegistryHiveKind.CurrentUser, UninstallKeyPath); }
        catch (Exception exception) { essential.Add("清理卸载登记失败：" + exception.Message); }
        try
        {
            shortcuts.Delete(StartMenuShortcutPath);
            shortcuts.Delete(DesktopShortcutPath);
        }
        catch (Exception exception) { essential.Add("清理快捷方式失败：" + exception.Message); }
        var moved = false;
        try
        {
            if (Directory.Exists(install)) Directory.Delete(install, recursive: true);
            if (hadPrevious)
            {
                Directory.Move(previous, install);
                moved = true;
            }
        }
        catch (Exception exception) { essential.Add("恢复旧安装失败：" + exception.Message); }
        if (!moved) return (essential, restored);

        // 旧安装已回到原位：登记与快捷方式也要回到旧版本，否则用户会看到「已安装但应用列表里没有」。
        try { WriteUninstallRegistration(install, previousVersion ?? ReadInstalledVersion(install) ?? version); }
        catch (Exception exception) { restored.Add("恢复旧版本卸载登记失败：" + exception.Message); }
        try { ApplyShortcuts(install, startMenu, desktop); }
        catch (Exception exception) { restored.Add("恢复旧安装快捷方式失败：" + exception.Message); }
        return (essential, restored);
    }

    /// <summary>读取安装目录里记录的版本号；缺失或为空时返回 null，由调用方决定兜底。</summary>
    private static string? ReadInstalledVersion(string install)
    {
        try
        {
            var path = Path.Combine(install, "version.txt");
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>卸载：保留用户数据，只删除套件安装目录、套件自己的卸载键、启动值与快捷方式。
    /// 从安装目录内的卸载入口运行时传入自身路径：先删其他文件与注册项，目录由调用方在进程退出后清理。</summary>
    public void Uninstall(string installDirectory, string? runningSetupPath = null)
    {
        var install = Normalize(installDirectory);
        var exePath = Path.Combine(install, SuiteIdentity.ExecutableName);
        var setupPath = Path.Combine(install, SuiteIdentity.SetupExecutableName);
        EnsureNotRunning(exePath);
        if (runningSetupPath is null) EnsureNotRunning(setupPath);
        if (Directory.Exists(install) && !File.Exists(exePath) && !File.Exists(setupPath)
            && !File.Exists(Path.Combine(install, "version.txt")))
            throw new InvalidOperationException("目标目录不包含工具套件安装文件，已停止卸载：" + install);
        // 删除登录任务失败时先停止卸载，保留完整程序以便重试，避免留下悬空启动项。
        startupTasks?.Delete(SuiteIdentity.RunValueName);
        if (Directory.Exists(install))
        {
            if (runningSetupPath is null) Directory.Delete(install, recursive: true);
            else
            {
                var keep = Path.GetFullPath(runningSetupPath);
                if (!keep.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("正在运行的卸载入口不在安装目录内。", nameof(runningSetupPath));
                DeleteContentsExcept(install, keep);
            }
        }
        // 只删套件自己的值名与卸载子键；原应用的同名区域不在写入范围内。
        registry.DeleteValue(RegistryHiveKind.CurrentUser, RunKeyPath, SuiteIdentity.RunValueName);
        registry.DeleteTree(RegistryHiveKind.CurrentUser, UninstallKeyPath);
        shortcuts.Delete(StartMenuShortcutPath);
        shortcuts.Delete(DesktopShortcutPath);
    }

    private void WriteUninstallRegistration(string install, string? versionOverride = null)
    {
        var setup = Path.Combine(install, SuiteIdentity.SetupExecutableName);
        registry.WriteString(RegistryHiveKind.CurrentUser, UninstallKeyPath, "DisplayName", SuiteIdentity.ProductName);
        registry.WriteString(RegistryHiveKind.CurrentUser, UninstallKeyPath, "DisplayVersion", versionOverride ?? version);
        registry.WriteString(RegistryHiveKind.CurrentUser, UninstallKeyPath, "Publisher", "ToolSuite");
        registry.WriteString(RegistryHiveKind.CurrentUser, UninstallKeyPath, "InstallLocation", install);
        registry.WriteString(RegistryHiveKind.CurrentUser, UninstallKeyPath, "DisplayIcon",
            Path.Combine(install, SuiteIdentity.ExecutableName) + ",0");
        registry.WriteString(RegistryHiveKind.CurrentUser, UninstallKeyPath, "UninstallString", $"\"{setup}\" /uninstall");
        registry.WriteString(RegistryHiveKind.CurrentUser, UninstallKeyPath, "QuietUninstallString", $"\"{setup}\" /uninstall /silent");
        registry.WriteDword(RegistryHiveKind.CurrentUser, UninstallKeyPath, "NoModify", 1);
        registry.WriteDword(RegistryHiveKind.CurrentUser, UninstallKeyPath, "NoRepair", 1);
    }

    private void ApplyShortcuts(string install, bool startMenu, bool desktop)
    {
        var target = Path.Combine(install, SuiteIdentity.ExecutableName);
        if (startMenu) shortcuts.Create(StartMenuShortcutPath, target, install, SuiteIdentity.ProductName);
        else shortcuts.Delete(StartMenuShortcutPath);
        if (desktop) shortcuts.Create(DesktopShortcutPath, target, install, SuiteIdentity.ProductName);
        else shortcuts.Delete(DesktopShortcutPath);
    }

    /// <summary>套件 EXE 正在运行或被占用时拒绝覆盖/删除，给出退出后重试的提示。</summary>
    private static void EnsureNotRunning(string path)
    {
        if (!File.Exists(path)) return;
        if (string.Equals(Path.GetFullPath(path), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallerBlockedException(
                $"工具套件正在运行，无法覆盖或删除 {Path.GetFileName(path)}。请从托盘退出工具套件后重试。", ex);
        }
    }

    private static void Extract(Stream payload, string destination)
    {
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包包含非法路径：" + entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string Normalize(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("安装目录不能为空。", nameof(directory));
        var full = Path.GetFullPath(directory);
        if (string.Equals(Path.GetPathRoot(full)?.TrimEnd('\\'), full.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("安装目录不能是磁盘根目录。", nameof(directory));
        return full;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>保留正在运行的卸载入口，删除其余安装内容；进程退出后由调用方清理空目录。</summary>
    private static void DeleteContentsExcept(string directory, string keep)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            if (!string.Equals(Path.GetFullPath(file), keep, StringComparison.OrdinalIgnoreCase))
                File.Delete(file);
        foreach (var child in Directory.EnumerateDirectories(directory))
            if (keep.StartsWith(child + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                DeleteContentsExcept(child, keep);
            else
                Directory.Delete(child, recursive: true);
    }
}
