using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using ToolSuite.Core;

namespace ToolSuite.Windows;

/// <summary>当前用户独享的交互式最高权限登录任务。无需保存密码，不创建后台服务。</summary>
public sealed class LogonTaskStore : IRunValueStore
{
    private readonly string userSid = WindowsIdentity.GetCurrent().User!.Value;
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    public string TaskName { get; }

    public LogonTaskStore() : this(null) { }

    // 隔离检查使用独立任务名，避免触碰用户的真实自启任务。
    internal LogonTaskStore(string? taskName)
    {
        TaskName = taskName ?? SuiteIdentity.RunValueName + "." + userSid;
    }

    public string? Read(string valueName)
    {
        CheckName(valueName);
        return WithFolder<string?>(folder =>
        {
            object? task = null;
            try
            {
                task = folder.GetTask(TaskName);
                dynamic registered = task;
                var xml = XDocument.Parse((string)registered.Xml);
                var root = xml.Root!;
                var principal = root.Element(Ns + "Principals")?.Element(Ns + "Principal");
                var trigger = root.Element(Ns + "Triggers")?.Element(Ns + "LogonTrigger");
                var actions = root.Element(Ns + "Actions");
                var exec = actions?.Element(Ns + "Exec");
                if (!(bool)registered.Enabled
                    || !IsCurrentUser(principal?.Element(Ns + "UserId")?.Value)
                    || principal?.Element(Ns + "LogonType")?.Value != "InteractiveToken"
                    || principal?.Element(Ns + "RunLevel")?.Value != "HighestAvailable"
                    || !IsCurrentUser(trigger?.Element(Ns + "UserId")?.Value)
                    || trigger?.Element(Ns + "Enabled")?.Value == "false"
                    || actions?.Elements().Count() != 1
                    || !string.IsNullOrWhiteSpace(exec?.Element(Ns + "Arguments")?.Value)) return null;
                var executable = exec?.Element(Ns + "Command")?.Value;
                return string.IsNullOrWhiteSpace(executable) ? null : '"' + executable + '"';
            }
            catch (Exception ex) when (IsMissingTask(ex)) { return null; }
            finally { Release(task); }
        });
    }

    public void Write(string valueName, string command)
    {
        CheckName(valueName);
        WithFolder(folder =>
        {
            // TASK_CREATE_OR_UPDATE=6, TASK_LOGON_INTERACTIVE_TOKEN=3。
            object registered = folder.RegisterTask(TaskName, CreateDefinition(command, userSid), 6,
                userSid, null, 3, null);
            Release(registered);
            return true;
        });
    }

    public void Delete(string valueName)
    {
        CheckName(valueName);
        WithFolder(folder =>
        {
            try { folder.DeleteTask(TaskName, 0); }
            catch (Exception ex) when (IsMissingTask(ex)) { }
            return true;
        });
    }

    public static string CreateDefinition(string command, string userSid)
    {
        var executable = command.Trim('"');
        if (!Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || executable.Contains('"')) throw new ArgumentException("登录任务必须指向完整 EXE 路径。", nameof(command));
        XElement E(string name, object value) => new(Ns + name, value);
        return new XDocument(new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Description", "工具套件：当前用户登录后恢复已启用工具。")),
            E("Triggers", new XElement(Ns + "LogonTrigger", E("Enabled", "true"), E("UserId", userSid))),
            E("Principals", new XElement(Ns + "Principal", new XAttribute("id", "CurrentUser"),
                E("UserId", userSid), E("LogonType", "InteractiveToken"), E("RunLevel", "HighestAvailable"))),
            E("Settings", new object[] {
                E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", "false"),
                E("StopIfGoingOnBatteries", "false"), E("StartWhenAvailable", "true"),
                E("Enabled", "true"), E("ExecutionTimeLimit", "PT0S") }),
            new XElement(Ns + "Actions", new XAttribute("Context", "CurrentUser"),
                E("Exec", new object[] { E("Command", executable),
                    E("WorkingDirectory", Path.GetDirectoryName(executable)!) })))).ToString();
    }

    private static void CheckName(string valueName)
    {
        if (valueName != SuiteIdentity.RunValueName) throw new ArgumentException("只能操作工具套件自己的登录任务。", nameof(valueName));
    }

    // COM 的 ERROR_FILE_NOT_FOUND 会被 .NET 映射为 FileNotFoundException。
    // 仅忽略“任务不存在”；权限不足、服务异常等仍须向上报告。
    private static bool IsMissingTask(Exception exception) =>
        exception is FileNotFoundException or COMException
        && exception.HResult == unchecked((int)0x80070002);

    private bool IsCurrentUser(string? account)
    {
        if (account == userSid) return true;
        if (string.IsNullOrWhiteSpace(account)) return false;
        // 任务计划程序可能将 XML 中的 SID 规范化为账户名。
        try { return new NTAccount(account).Translate(typeof(SecurityIdentifier)).Value == userSid; }
        catch (IdentityNotMappedException) { return false; }
    }

    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        object service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        object? folder = null;
        try
        {
            dynamic scheduler = service;
            scheduler.Connect();
            folder = scheduler.GetFolder(@"\");
            return action(folder);
        }
        finally { Release(folder); Release(service); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
