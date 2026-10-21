using ToolSuite.Core;
using Color = System.Drawing.Color;
using Forms = System.Windows.Forms;

namespace ToolSuite.App;

internal sealed class TrayHost : IDisposable
{
    private readonly SuiteController controller;
    private readonly Action showSettings;
    private readonly Func<Task> exit;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.NotifyIcon icon;
    private bool darkAppearance;
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
            var group = new Forms.ToolStripMenuItem($"{tool.DisplayName} · {ShortState(tool)}") { Name = tool.Id };
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
        ApplyItemColors();
    }
    /// <summary>宿主主题变化时刷新托盘外观；暗色自定义配色，高对比度交还系统配色保证可读。</summary>
    internal void ApplyAppearance(bool dark, bool highContrast)
    {
        darkAppearance = dark && !highContrast;
        if (highContrast)
        {
            menu.RenderMode = Forms.ToolStripRenderMode.System;
            menu.BackColor = System.Drawing.SystemColors.Window;
            menu.ForeColor = System.Drawing.SystemColors.WindowText;
        }
        else if (darkAppearance)
        {
            menu.RenderMode = Forms.ToolStripRenderMode.Professional;
            menu.Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColorTable());
            menu.BackColor = Color.FromArgb(32, 32, 32);
            menu.ForeColor = Color.White;
        }
        else
        {
            menu.RenderMode = Forms.ToolStripRenderMode.Professional;
            menu.Renderer = new Forms.ToolStripProfessionalRenderer();
            menu.BackColor = System.Drawing.SystemColors.Menu;
            menu.ForeColor = System.Drawing.SystemColors.MenuText;
        }
        ApplyItemColors();
    }
    private void ApplyItemColors()
    {
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = menu.ForeColor;
            if (item is not Forms.ToolStripMenuItem group) continue;
            foreach (Forms.ToolStripItem child in group.DropDownItems) child.ForeColor = menu.ForeColor;
        }
    }
    internal static string ShortState(ToolStatus tool) => ToolStateText.Short(tool.State);
    internal Forms.ContextMenuStrip MenuForVerification => menu;
    internal void RefreshMenuForVerification() => RebuildMenu();
    internal void InvokeSettingsMenuForVerification() => menu.Items["settings"]!.PerformClick();
    internal void InvokeExitMenuForVerification() => menu.Items["exit"]!.PerformClick();
    public void ShowError(string message)
    {
        icon.BalloonTipTitle = "工具套件操作失败"; icon.BalloonTipText = message; icon.BalloonTipIcon = Forms.ToolTipIcon.Error;
        icon.ShowBalloonTip(5000);
    }
    public void Dispose() { icon.Visible = false; icon.Dispose(); menu.Dispose(); }

    private sealed class DarkColorTable : Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(32, 32, 32);
        public override Color MenuBorder => Color.FromArgb(70, 70, 70);
        public override Color MenuItemBorder => Color.FromArgb(80, 80, 80);
        public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 60);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 60);
        public override Color ImageMarginGradientBegin => Color.FromArgb(32, 32, 32);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(32, 32, 32);
        public override Color ImageMarginGradientEnd => Color.FromArgb(32, 32, 32);
        public override Color SeparatorDark => Color.FromArgb(70, 70, 70);
        public override Color SeparatorLight => Color.FromArgb(32, 32, 32);
    }
}
