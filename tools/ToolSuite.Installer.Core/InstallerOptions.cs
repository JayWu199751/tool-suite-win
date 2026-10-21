namespace ToolSuite.Installer;

public enum InstallerMode { Interactive, Install, Uninstall }

public sealed record InstallerOptions(InstallerMode Mode, bool Silent, string InstallDirectory, bool StartMenu, bool Desktop)
{
    public static InstallerOptions Parse(string[] args)
    {
        var mode = InstallerMode.Interactive;
        var silent = false;
        var startMenu = true;
        var desktop = false;
        string? directory = null;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("/silent", StringComparison.OrdinalIgnoreCase) || argument == "-s")
                silent = true;
            else if (argument.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
                mode = InstallerMode.Uninstall;
            else if (argument.Equals("/install", StringComparison.OrdinalIgnoreCase))
                mode = InstallerMode.Install;
            else if (argument.Equals("/desktop", StringComparison.OrdinalIgnoreCase))
                desktop = true;
            else if (argument.Equals("/no-start-menu", StringComparison.OrdinalIgnoreCase))
                startMenu = false;
            else if (argument.StartsWith("/dir=", StringComparison.OrdinalIgnoreCase))
                directory = argument["/dir=".Length..];
            else if (argument.Equals("/dir", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                directory = args[++index];
        }
        return new InstallerOptions(mode, silent, directory ?? InstallerEngine.DefaultInstallDirectory, startMenu, desktop);
    }
}
