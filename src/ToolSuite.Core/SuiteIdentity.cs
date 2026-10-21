namespace ToolSuite.Core;

/// <summary>套件独立身份：单实例、自启、安装与卸载只使用这些名字，不接管原三个应用的身份。</summary>
public static class SuiteIdentity
{
    public const string ProductName = "工具套件";
    public const string ExecutableName = "ToolSuite.exe";
    public const string SetupExecutableName = "ToolSuite.Setup.exe";
    public const string UninstallKeyName = "ToolSuite";
    /// <summary>HKCU Run 下套件自己的值名；不得复用或删除原应用的启动值。</summary>
    public const string RunValueName = "ToolSuite.Suite";
    public const string DataFolderName = "ToolSuite";
    public const string Version = "1.0.0";
}
