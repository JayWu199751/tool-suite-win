using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ToolSuite.Core;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Forms = System.Windows.Forms;

namespace ToolSuite.App;

internal sealed class TrayHost : IDisposable
{
    private readonly SuiteController controller;
    private readonly Action showSettings;
    private readonly Func<Task> exit;
    private readonly Forms.NotifyIcon icon;
    private readonly TrayMenu menu = new();
    private readonly Icon? trayIcon;
    /// <summary>防闪重开判定：收起与右键按下的时间戳乱序容差（毫秒）。</summary>
    private const int ReopenSuppressToleranceMs = 100;
    private long rightDownAt = TrayMenu.Never;

    public TrayHost(SuiteController controller, Action showSettings, Func<Task> exit)
    {
        this.controller = controller; this.showSettings = showSettings; this.exit = exit;
        trayIcon = LoadTrayIcon();
        icon = new Forms.NotifyIcon
        {
            Text = "工具套件",
            Icon = trayIcon ?? System.Drawing.SystemIcons.Application,
            Visible = true
        };
        // 左键单击/双击不打开任何工具；只有右键弹出菜单。
        icon.MouseDown += (_, e) => { if (e.Button == Forms.MouseButtons.Right) rightDownAt = Environment.TickCount64; };
        icon.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Right) ShowMenu(); };
        BuildMenu();
    }

    /// <summary>右键打开菜单；已打开时先收起（再次右键等于取消）。
    /// 菜单获得前台后，右键按下的瞬间菜单可能已被"点击外部收起"顶下去，这记右键只算收起不重开。</summary>
    private void ShowMenu()
    {
        if (menu.IsOpen) { menu.IsOpen = false; return; }
        if (!ShouldOpenAfterRightClick(menu.ClosedAtTick, rightDownAt)) return;
        BuildMenu();
        menu.IsOpen = true;
    }

    /// <summary>收起与右键按下几乎同时（含消息乱序容差）说明菜单正是被这记点击顶下去的，不得重开；
    /// 没有按下记录（程序化打开）或早已收起则正常打开。</summary>
    internal static bool ShouldOpenAfterRightClick(long closedAtTick, long rightDownAtTick) =>
        rightDownAtTick == TrayMenu.Never || closedAtTick + ReopenSuppressToleranceMs < rightDownAtTick;

    private void BuildMenu()
    {
        menu.IsOpen = false;
        menu.Items.Clear();
        foreach (var tool in controller.Tools)
        {
            // WPF 的 Name 不接受连字符（工具 id 形如 perf-monitor），用 Tag 承载逻辑 id。
            var group = new MenuItem { Tag = tool.Id, Header = TrayMenu.GroupHeader(tool.DisplayName, ShortState(tool)) };
            var toggle = new MenuItem
            {
                Header = tool.State == ToolState.Failed ? "重新启用" : tool.Enabled ? "停用" : "启用",
                IsEnabled = tool.State != ToolState.Unavailable,
            };
            toggle.Click += async (_, e) =>
            {
                e.Handled = true;
                await controller.SetEnabledAsync(tool.Id, tool.State == ToolState.Failed || !tool.Enabled);
            };
            group.Items.Add(toggle);
            foreach (var command in tool.Commands)
            {
                var item = new MenuItem
                {
                    Header = command.Label,
                    IsEnabled = !command.RequiresRunning || tool.State == ToolState.Running,
                };
                item.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await controller.ExecuteCommandAsync(tool.Id, command.Id);
                };
                group.Items.Add(item);
            }
            menu.Items.Add(group);
        }
        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = "统一设置", Tag = "settings" };
        settings.Click += (_, e) => { e.Handled = true; showSettings(); };
        menu.Items.Add(settings);
        var quit = new MenuItem { Header = "退出工具套件", Tag = "exit" };
        quit.Click += async (_, e) => { e.Handled = true; await exit(); };
        menu.Items.Add(quit);
    }

    /// <summary>主题与高对比度变化都由宿主在这里统一下发给菜单。</summary>
    internal void ApplyAppearance(bool dark, bool highContrast) => menu.ApplyAppearance(highContrast);

    internal static string ShortState(ToolStatus tool) => ToolStateText.Short(tool.State);

    /// <summary>托盘图标取 ICO 内与系统小图标尺寸匹配的帧（16/20/24/32），避免 GDI 缩放发虚。
    /// 资源用组件 URI 指向 ToolSuite 程序集，检查宿主从别的入口程序集启动时也能解析。</summary>
    private static Icon? LoadTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/ToolSuite;component/Assets/app.ico", UriKind.Absolute));
            if (resource?.Stream is null) return null;
            using var stream = resource.Stream;
            return new Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // 图标缺失不应阻止托盘启动；退回系统图标。
            return null;
        }
    }

    internal ContextMenu MenuForVerification => menu;
    internal Icon? TrayIconForVerification => trayIcon;
    internal IntPtr MenuPopupHandleForVerification => menu.PopupHandle;
    internal void RefreshMenuForVerification() => BuildMenu();
    /// <summary>检查宿主用：走真实打开路径弹菜单；清掉按下记录以免被防闪重开判定拦截。</summary>
    internal void OpenMenuForVerification()
    {
        rightDownAt = TrayMenu.Never;
        ShowMenu();
    }
    internal void InvokeSettingsMenuForVerification() => Click("settings");
    internal void InvokeExitMenuForVerification() => Click("exit");

    private void Click(string name)
    {
        var item = menu.Items.OfType<MenuItem>().FirstOrDefault(x => (string?)x.Tag == name);
        item?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    public void ShowError(string message)
    {
        icon.BalloonTipTitle = "工具套件操作失败"; icon.BalloonTipText = message; icon.BalloonTipIcon = Forms.ToolTipIcon.Error;
        icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        menu.IsOpen = false;
        menu.Items.Clear();
        icon.Visible = false;
        icon.Dispose();
        trayIcon?.Dispose();
    }
}
