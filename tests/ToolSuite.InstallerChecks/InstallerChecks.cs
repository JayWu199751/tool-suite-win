using System.IO;
using System.IO.Compression;
using ToolSuite.Core;
using ToolSuite.Installer;

namespace ToolSuite.InstallerChecks;

/// <summary>内存注册表：记录写入/删除目标，用于断言安装器只动套件自己的卸载键与启动值。</summary>
internal sealed class FakeRegistry : IRegistryAccess
{
    public Dictionary<string, Dictionary<string, string>> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Operations { get; } = new();

    public string? ReadString(RegistryHiveKind hive, string subKey, string valueName) =>
        Values.TryGetValue(Key(hive, subKey), out var values) ? values.GetValueOrDefault(valueName) : null;

    public void WriteString(RegistryHiveKind hive, string subKey, string valueName, string value)
    {
        Operations.Add($"write {Key(hive, subKey)} {valueName}");
        GetOrCreate(Key(hive, subKey))[valueName] = value;
    }

    public void WriteDword(RegistryHiveKind hive, string subKey, string valueName, int value) =>
        WriteString(hive, subKey, valueName, value.ToString());

    public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName)
    {
        Operations.Add($"delete-value {Key(hive, subKey)} {valueName}");
        if (Values.TryGetValue(Key(hive, subKey), out var values)) values.Remove(valueName);
    }

    public void DeleteTree(RegistryHiveKind hive, string subKey)
    {
        var key = Key(hive, subKey);
        Operations.Add("delete-tree " + key);
        foreach (var existing in Values.Keys.Where(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)
                     || x.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase)).ToArray())
            Values.Remove(existing);
    }

    public Dictionary<string, string> Seed(RegistryHiveKind hive, string subKey)
    {
        var values = GetOrCreate(Key(hive, subKey));
        return values;
    }

    private Dictionary<string, string> GetOrCreate(string key)
    {
        if (!Values.TryGetValue(key, out var values)) Values[key] = values = new(StringComparer.OrdinalIgnoreCase);
        return values;
    }

    private static string Key(RegistryHiveKind hive, string subKey) => hive + "\\" + subKey;
}

/// <summary>可控失败注册表：写入指定值名时抛异常，用于模拟卸载登记半写。
/// 默认一次性失败（瞬时写入错误），可传 <paramref name="failTimes"/> 模拟持续失败。</summary>
internal sealed class FailingRegistry(FakeRegistry inner, string failOnValueName, int failTimes = 1) : IRegistryAccess
{
    private int remaining = failTimes;

    public string? ReadString(RegistryHiveKind hive, string subKey, string valueName) =>
        inner.ReadString(hive, subKey, valueName);

    public void WriteString(RegistryHiveKind hive, string subKey, string valueName, string value)
    {
        if (valueName == failOnValueName && remaining > 0)
        {
            remaining--;
            throw new IOException("模拟写入失败：" + valueName);
        }
        inner.WriteString(hive, subKey, valueName, value);
    }

    public void WriteDword(RegistryHiveKind hive, string subKey, string valueName, int value) =>
        WriteString(hive, subKey, valueName, value.ToString());

    public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName) =>
        inner.DeleteValue(hive, subKey, valueName);

    public void DeleteTree(RegistryHiveKind hive, string subKey) => inner.DeleteTree(hive, subKey);
}

internal sealed class FakeShortcutWriter : IShortcutWriter
{
    public List<(string Path, string Target)> Created { get; } = new();
    public List<string> Deleted { get; } = new();
    /// <summary>非空时 Create 抛该异常，模拟快捷方式写入失败。</summary>
    public Exception? CreateFailure { get; init; }
    /// <summary>抛出前执行（用于制造无法删除的新安装目录）。</summary>
    public Action<string>? BeforeCreate { get; init; }
    public void Create(string shortcutPath, string targetPath, string workingDirectory, string description)
    {
        BeforeCreate?.Invoke(targetPath);
        if (CreateFailure is not null) throw CreateFailure;
        Created.Add((shortcutPath, targetPath));
    }
    public void Delete(string shortcutPath) => Deleted.Add(shortcutPath);
}

/// <summary>临时目录中的真实安装/升级/卸载检查，配可控注册表与快捷方式，不触碰真实安装路径或旧应用。</summary>
internal static class InstallerChecks
{
    internal static void Run(string root, List<string> log)
    {
        Directory.CreateDirectory(root);
        InstallIntoOwnScope(root, log);
        UpgradeRefusesWhileRunning(root, log);
        UpgradeRegistrationFailureKeepsOldInstall(root, log);
        FreshInstallShortcutFailureRemovesInstall(root, log);
        RollbackFailureKeepsOriginalError(root, log);
        UninstallKeepsUserData(root, log);
        Arguments(log);
    }

    private static void InstallIntoOwnScope(string root, List<string> log)
    {
        var baseDir = Path.Combine(root, "install");
        var install = Path.Combine(baseDir, "apps", "ToolSuite");
        var registry = new FakeRegistry();
        SeedOldApplications(registry);
        var shortcuts = new FakeShortcutWriter();
        var setup = CreateSetup(baseDir);
        var engine = new InstallerEngine(registry, shortcuts, setup, "1.0.0");

        var result = engine.Install(Payload(baseDir, "1.0.0"), install, startMenu: true, desktop: true);

        Assert(!result.Upgraded, "首次安装不得报告为升级");
        Assert(File.ReadAllText(Path.Combine(install, "ToolSuite.exe")) == "exe-1.0.0", "安装目录必须包含发布产物");
        Assert(File.Exists(Path.Combine(install, "ToolSuite.Setup.exe")), "安装目录必须包含可用的卸载入口");
        Assert(File.Exists(Path.Combine(install, "runtimes", "PresentationFramework.dll")), "自包含运行时文件必须完整解压");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayName") == "工具套件",
            "必须写入套件自己的卸载信息");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayVersion") == "1.0.0",
            "卸载信息必须记录安装版本");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "InstallLocation") == install,
            "卸载信息必须指向套件安装目录");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "UninstallString")!
            .Contains("ToolSuite.Setup.exe\" /uninstall"), "卸载命令必须调用套件自己的卸载入口");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.RunKeyPath, SuiteIdentity.RunValueName) is null,
            "安装不得默认开启开机启动");
        AssertOldApplicationsIntact(registry);
        Assert(shortcuts.Created.Any(x => x.Path == InstallerEngine.StartMenuShortcutPath && x.Target == Path.Combine(install, "ToolSuite.exe")),
            "开始菜单快捷方式必须指向安装后的正式 EXE");
        Assert(shortcuts.Created.Any(x => x.Path == InstallerEngine.DesktopShortcutPath), "按需创建桌面快捷方式");
        Assert(registry.Operations.All(x => x.Contains("Uninstall\\ToolSuite", StringComparison.OrdinalIgnoreCase)
            || x.Contains(SuiteIdentity.RunValueName, StringComparison.OrdinalIgnoreCase)),
            "安装只能写入套件自己的卸载键：" + string.Join(";", registry.Operations));
        log.Add("安装到套件自己的临时目录，写入卸载信息与可选快捷方式，不写启动项、不触碰原应用");
    }

    private static void UpgradeRefusesWhileRunning(string root, List<string> log)
    {
        var baseDir = Path.Combine(root, "upgrade");
        var install = Path.Combine(baseDir, "apps", "ToolSuite");
        var registry = new FakeRegistry();
        var shortcuts = new FakeShortcutWriter();
        var v1 = new InstallerEngine(registry, shortcuts, CreateSetup(baseDir), "1.0.0");
        v1.Install(Payload(baseDir, "1.0.0"), install, startMenu: false, desktop: false);

        var exe = Path.Combine(install, "ToolSuite.exe");
        using (new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var blocked = Throws<InstallerBlockedException>(() =>
                new InstallerEngine(registry, shortcuts, CreateSetup(baseDir), "1.1.0")
                    .Install(Payload(baseDir, "1.1.0"), install, startMenu: false, desktop: false));
            Assert(blocked.CanRetry, "占用拒绝必须允许重试");
            Assert(blocked.Message.Contains("退出") && blocked.Message.Contains("重试"),
                "占用提示必须说明退出后重试：" + blocked.Message);
            Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayVersion") == "1.0.0",
                "被占用时不得提前改写卸载版本");
        }
        Assert(File.ReadAllText(exe) == "exe-1.0.0", "被占用时不得覆盖旧版本文件");

        var upgraded = new InstallerEngine(registry, shortcuts, CreateSetup(baseDir), "1.1.0")
            .Install(Payload(baseDir, "1.1.0"), install, startMenu: false, desktop: false);
        Assert(upgraded.Upgraded, "已有安装应报告为升级");
        Assert(File.ReadAllText(exe) == "exe-1.1.0", "退出后重试必须完成升级");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayVersion") == "1.1.0",
            "升级必须更新卸载信息版本");
        log.Add("套件文件被占用时拒绝升级并提示退出后重试，解除占用后可重试升级");
    }

    /// <summary>升级时卸载登记写入失败：旧安装必须原样留在安装目录，登记与快捷方式回到旧版本状态。</summary>
    private static void UpgradeRegistrationFailureKeepsOldInstall(string root, List<string> log)
    {
        var baseDir = Path.Combine(root, "rollback-upgrade");
        var install = Path.Combine(baseDir, "apps", "ToolSuite");
        var registry = new FakeRegistry();
        var shortcuts = new FakeShortcutWriter();
        new InstallerEngine(registry, shortcuts, CreateSetup(baseDir), "1.0.0")
            .Install(Payload(baseDir, "1.0.0"), install, startMenu: true, desktop: false);

        var failing = new FailingRegistry(registry, "DisplayVersion");
        Throws<IOException>(() => new InstallerEngine(failing, shortcuts, CreateSetup(baseDir), "1.1.0")
            .Install(Payload(baseDir, "1.1.0"), install, startMenu: true, desktop: false));

        Assert(File.ReadAllText(Path.Combine(install, "ToolSuite.exe")) == "exe-1.0.0",
            "升级失败必须把旧版本文件恢复到原安装目录");
        Assert(File.ReadAllText(Path.Combine(install, "version.txt")) == "1.0.0", "升级失败必须恢复旧版本号");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayName") == SuiteIdentity.ProductName,
            "升级失败必须把卸载登记恢复为旧安装，而不是留下半写或空白条目");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayVersion") == "1.0.0",
            "升级失败后卸载登记必须显示旧版本号");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "InstallLocation") == install,
            "升级失败后卸载登记的安装位置必须仍指向原目录");
        Assert(LeftoverDirectories(install) == 0, "升级失败不得遗留暂存或备份目录");
        Assert(shortcuts.Deleted.Contains(InstallerEngine.StartMenuShortcutPath), "升级失败必须清理指向新安装的快捷方式");
        Assert(shortcuts.Created.Count(x => x.Path == InstallerEngine.StartMenuShortcutPath) == 2,
            "升级失败必须按旧安装重建开始菜单快捷方式");
        log.Add("升级写卸载登记失败时恢复旧安装、旧版本登记与快捷方式");
    }

    /// <summary>全新安装时快捷方式写入失败：删除新安装目录并清理已写入的卸载键。</summary>
    private static void FreshInstallShortcutFailureRemovesInstall(string root, List<string> log)
    {
        var baseDir = Path.Combine(root, "rollback-fresh");
        var install = Path.Combine(baseDir, "apps", "ToolSuite");
        var registry = new FakeRegistry();
        var shortcuts = new FakeShortcutWriter { CreateFailure = new UnauthorizedAccessException("模拟快捷方式写入失败") };
        Throws<UnauthorizedAccessException>(() => new InstallerEngine(registry, shortcuts, CreateSetup(baseDir), "1.0.0")
            .Install(Payload(baseDir, "1.0.0"), install, startMenu: true, desktop: false));

        Assert(!Directory.Exists(install), "全新安装失败不得保留新安装目录");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayName") is null,
            "全新安装失败必须清理已写入的卸载键");
        Assert(LeftoverDirectories(install) == 0, "全新安装失败不得遗留暂存目录");
        log.Add("全新安装写快捷方式失败时删除新安装目录并清理卸载键");
    }

    /// <summary>回滚自身失败：保留原始异常作为内部异常，并在消息中说明残留位置。</summary>
    private static void RollbackFailureKeepsOriginalError(string root, List<string> log)
    {
        var baseDir = Path.Combine(root, "rollback-failed");
        var install = Path.Combine(baseDir, "apps", "ToolSuite");
        var registry = new FakeRegistry();
        var locked = new List<FileStream>();
        var shortcuts = new FakeShortcutWriter
        {
            CreateFailure = new UnauthorizedAccessException("模拟快捷方式写入失败"),
            // 占住新安装产物，让回滚删除目录必然失败。
            BeforeCreate = target => locked.Add(new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        };
        try
        {
            var thrown = Throws<InvalidOperationException>(() => new InstallerEngine(registry, shortcuts, CreateSetup(baseDir), "1.0.0")
                .Install(Payload(baseDir, "1.0.0"), install, startMenu: true, desktop: false));
            Assert(thrown.InnerException is UnauthorizedAccessException, "回滚失败不得掩盖原始异常：" + thrown.InnerException);
            Assert(thrown.Message.Contains(install), "回滚失败必须说明残留位置：" + thrown.Message);
        }
        finally
        {
            foreach (var stream in locked) stream.Dispose();
            if (Directory.Exists(install)) Directory.Delete(install, true);
        }
        log.Add("回滚自身失败时保留原始异常并说明残留位置");
    }

    private static void UninstallKeepsUserData(string root, List<string> log)
    {
        var baseDir = Path.Combine(root, "uninstall");
        var install = Path.Combine(baseDir, "apps", "ToolSuite");
        var registry = new FakeRegistry();
        SeedOldApplications(registry);
        var shortcuts = new FakeShortcutWriter();
        var startupTasks = new FakeRunStore();
        var engine = new InstallerEngine(registry, shortcuts, CreateSetup(baseDir), "1.0.0", startupTasks);
        engine.Install(Payload(baseDir, "1.0.0"), install, startMenu: true, desktop: false);

        var data = Path.Combine(baseDir, "user-data", "ToolSuite");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "suite.json"), """{"theme":"Dark"}""");
        var oldData = Path.Combine(baseDir, "old-data", "ClipboardTool");
        Directory.CreateDirectory(oldData);
        File.WriteAllText(Path.Combine(oldData, "history.db"), "旧应用数据");
        // 模拟用户已开启套件自启：卸载只删除这个值名。
        registry.WriteString(RegistryHiveKind.CurrentUser, InstallerEngine.RunKeyPath, SuiteIdentity.RunValueName,
            '"' + Path.Combine(install, "ToolSuite.exe") + '"');

        var exe = Path.Combine(install, "ToolSuite.exe");
        using (new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var blocked = Throws<InstallerBlockedException>(() => engine.Uninstall(install));
            Assert(blocked.Message.Contains("退出") && blocked.Message.Contains("重试"), "卸载占用提示必须说明退出后重试");
            Assert(Directory.Exists(install), "被占用时不得删除安装目录");
        }

        startupTasks.Values[SuiteIdentity.RunValueName] = '"' + exe + '"';
        startupTasks.FailDeletes = true;
        Throws<UnauthorizedAccessException>(() => engine.Uninstall(install));
        Assert(File.Exists(exe), "清理登录任务失败必须保留安装文件，以便提权后重试");
        startupTasks.FailDeletes = false;
        engine.Uninstall(install);
        Assert(!startupTasks.Values.ContainsKey(SuiteIdentity.RunValueName), "卸载必须清理登录计划任务");
        Assert(!Directory.Exists(install), "卸载必须删除套件安装目录");
        Assert(Directory.GetDirectories(Path.GetDirectoryName(install)!, "ToolSuite.*").Length == 0, "卸载不得遗留暂存或备份目录");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayName") is null,
            "卸载必须删除套件自己的卸载信息");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.RunKeyPath, SuiteIdentity.RunValueName) is null,
            "卸载必须删除套件自己的启动值");
        Assert(File.Exists(Path.Combine(data, "suite.json"))
            && File.ReadAllText(Path.Combine(data, "suite.json")).Contains("Dark"), "卸载必须保留套件用户数据");
        Assert(File.Exists(Path.Combine(oldData, "history.db")), "卸载不得触碰原应用数据");
        AssertOldApplicationsIntact(registry);
        Assert(shortcuts.Deleted.Contains(InstallerEngine.StartMenuShortcutPath), "卸载必须删除套件快捷方式");
        log.Add("卸载拒绝占用中的套件，成功后删除安装目录与套件注册项并保留用户数据与原应用数据");

        // 从安装目录内的卸载入口运行时，只能清理其余内容；目录由调用方在进程退出后删除。
        engine.Install(Payload(baseDir, "1.0.0"), install, startMenu: false, desktop: false);
        var runningSetup = Path.Combine(install, "ToolSuite.Setup.exe");
        engine.Uninstall(install, runningSetup);
        Assert(Directory.Exists(install) && File.Exists(runningSetup), "运行中的卸载入口应保留到进程退出");
        Assert(Directory.GetFileSystemEntries(install).Length == 1, "除正在运行的卸载入口外必须全部删除");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser, InstallerEngine.UninstallKeyPath, "DisplayName") is null,
            "自卸载入口运行也必须清理卸载信息");
        Directory.Delete(install, true);
        log.Add("卸载入口自身运行时保留自身并清理其余安装内容与注册项");
    }

    private static void Arguments(List<string> log)
    {
        var silent = InstallerOptions.Parse(["/silent", "/uninstall", "/dir=C:\\Tools\\Suite", "/desktop"]);
        Assert(silent is { Mode: InstallerMode.Uninstall, Silent: true, Desktop: true }, "静默卸载参数解析");
        Assert(Path.GetFullPath(silent.InstallDirectory) == Path.GetFullPath(@"C:\Tools\Suite"), "自定义安装目录解析");
        var defaults = InstallerOptions.Parse([]);
        Assert(defaults is { Mode: InstallerMode.Interactive, Silent: false, StartMenu: true, Desktop: false },
            "默认交互安装参数");
        Assert(InstallerEngine.DefaultInstallDirectory.EndsWith(Path.Combine("Programs", "ToolSuite")),
            "默认安装目录位于用户自己的 Programs 下：" + InstallerEngine.DefaultInstallDirectory);
        log.Add("安装器命令参数支持 install/upgrade/uninstall/silent 与自定义目录");
    }

    /// <summary>安装目录旁的暂存/备份目录数；按名字比较，不用通配符（Windows 通配符会连安装目录本身一起匹配）。</summary>
    private static int LeftoverDirectories(string install) =>
        Directory.GetDirectories(Path.GetDirectoryName(install)!)
            .Count(path => !string.Equals(Path.GetFileName(path), Path.GetFileName(install), StringComparison.OrdinalIgnoreCase));

    private static void SeedOldApplications(FakeRegistry registry)
    {
        var run = registry.Seed(RegistryHiveKind.CurrentUser, InstallerEngine.RunKeyPath);
        run["ClipboardTool"] = @"""C:\Old\Clipboard\ClipboardTool.exe""";
        run["PerfMonitor"] = @"""C:\Old\Perf\PerfMonitor.exe""";
        run["Pinto"] = @"""C:\Old\Pinto\Pinto.exe""";
        var oldUninstall = registry.Seed(RegistryHiveKind.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipboardTool");
        oldUninstall["DisplayName"] = "剪贴板历史";
        oldUninstall["UninstallString"] = @"""C:\Old\Clipboard\uninstall.exe""";
    }

    private static void AssertOldApplicationsIntact(FakeRegistry registry)
    {
        var run = registry.Values[RegistryHiveKind.CurrentUser + "\\" + InstallerEngine.RunKeyPath];
        Assert(run["ClipboardTool"] == @"""C:\Old\Clipboard\ClipboardTool.exe"""
            && run["PerfMonitor"] == @"""C:\Old\Perf\PerfMonitor.exe"""
            && run["Pinto"] == @"""C:\Old\Pinto\Pinto.exe""", "不得修改或删除原三个应用的启动项");
        Assert(registry.ReadString(RegistryHiveKind.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipboardTool", "DisplayName") == "剪贴板历史",
            "不得修改原应用的卸载信息");
    }

    private static string CreateSetup(string baseDir)
    {
        Directory.CreateDirectory(baseDir);
        var setup = Path.Combine(baseDir, "download", "ToolSuite-Setup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(setup)!);
        File.WriteAllText(setup, "setup-payload");
        return setup;
    }

    private static Func<Stream> Payload(string baseDir, string version)
    {
        var source = Path.Combine(baseDir, "payload-" + version);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "ToolSuite.exe"), "exe-" + version);
        File.WriteAllText(Path.Combine(source, "coreclr.dll"), "runtime-" + version);
        Directory.CreateDirectory(Path.Combine(source, "runtimes"));
        File.WriteAllText(Path.Combine(source, "runtimes", "PresentationFramework.dll"), "wpf");
        var zip = Path.Combine(baseDir, "payload-" + version + ".zip");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(source, zip);
        return () => File.OpenRead(zip);
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new Exception("预期抛出 " + typeof(T).Name + "。");
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
