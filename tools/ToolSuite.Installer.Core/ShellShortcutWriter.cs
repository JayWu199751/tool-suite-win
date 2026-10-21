namespace ToolSuite.Installer;

/// <summary>通过系统 WScript.Shell 创建 .lnk；只写用户自己的开始菜单与桌面。</summary>
public sealed class ShellShortcutWriter : IShortcutWriter
{
    public void Create(string shortcutPath, string targetPath, string workingDirectory, string description)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("系统缺少创建快捷方式所需组件。");
        dynamic shell = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("无法初始化快捷方式组件。");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            dynamic link = shell.CreateShortcut(shortcutPath);
            link.TargetPath = targetPath;
            link.WorkingDirectory = workingDirectory;
            link.Description = description;
            link.Save();
        }
        finally
        {
            if (System.Runtime.InteropServices.Marshal.IsComObject(shell))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    public void Delete(string shortcutPath)
    {
        if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
    }
}
