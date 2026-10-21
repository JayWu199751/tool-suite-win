using ToolSuite.Core;
using Forms = System.Windows.Forms;

namespace ToolSuite.App;

internal sealed class TrayHost : IDisposable
{
    private readonly SuiteController controller;
    private readonly Action showSettings;
    private readonly Func<Task> exit;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.NotifyIcon icon;
    public TrayHost(SuiteController controller, Action showSettings, Func<Task> exit)
    {
        this.controller = controller; this.showSettings = showSettings; this.exit = exit;
        icon = new Forms.NotifyIcon
        {
            Text = "工具套件", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu, Visible = true
        };
        // 不订阅 Click / MouseClick / DoubleClick：左键与双击均不打开任何工具。
        menu.Opening += (_, _) => RebuildMenu();
        RebuildMenu();
    }
    private void RebuildMenu()
    {
        foreach (Forms.ToolStripItem item in menu.Items.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        foreach (var tool in controller.Tools)
        {
            var group = new Forms.ToolStripMenuItem(tool.DisplayName);
            group.DropDownItems.Add(new Forms.ToolStripMenuItem(SettingsWindow.StateLabel(tool)) { Enabled = false });
            var toggle = new Forms.ToolStripMenuItem(tool.State == ToolState.Failed ? "重新启用" : tool.Enabled ? "停用" : "启用")
                { Enabled = tool.State != ToolState.Unavailable };
            toggle.Click += async (_, _) => await controller.SetEnabledAsync(tool.Id, tool.State == ToolState.Failed || !tool.Enabled);
            group.DropDownItems.Add(toggle);
            foreach (var command in tool.Commands)
            {
                var item = new Forms.ToolStripMenuItem(command.Label)
                    { Enabled = !command.RequiresRunning || tool.State == ToolState.Running };
                item.Click += async (_, _) => await controller.ExecuteCommandAsync(tool.Id, command.Id);
                group.DropDownItems.Add(item);
            }
            menu.Items.Add(group);
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("统一设置", null, (_, _) => showSettings()).Name = "settings";
        menu.Items.Add("退出工具套件", null, async (_, _) => await exit()).Name = "exit";
    }
    internal void InvokeSettingsMenuForVerification() => menu.Items["settings"]!.PerformClick();
    internal void InvokeExitMenuForVerification() => menu.Items["exit"]!.PerformClick();
    public void ShowError(string message)
    {
        icon.BalloonTipTitle = "工具套件操作失败"; icon.BalloonTipText = message; icon.BalloonTipIcon = Forms.ToolTipIcon.Error;
        icon.ShowBalloonTip(5000);
    }
    public void Dispose() { icon.Visible = false; icon.Dispose(); menu.Dispose(); }
}
