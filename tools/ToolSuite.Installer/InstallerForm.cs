using System.Drawing.Drawing2D;

namespace ToolSuite.Installer;

/// <summary>安装 / 卸载对话框。视觉语言见 docs/design-system.md：
/// 浅色画布 (#F5F5F7)、白色 20 DIP 圆角卡片、主操作蓝色胶囊、次操作幽灵胶囊、零投影。
///
/// 整个客户区由本窗口一次画完（唯一子控件是安装目录输入框）：
/// 之前把卡片/按钮/勾选各自做成自绘子控件时，多个控件的表面会互相覆盖，
/// 悬浮重绘后留下成片重叠的文字与按钮；单面绘制从根上消除了这类合成问题。
/// 所有尺寸以 96 DPI 的 DIP 写死，再按 DeviceDpi 统一换算，字号与几何始终同尺度。</summary>
internal sealed class InstallerForm : Form
{
    private enum Target { None, Retry, Uninstall, Close, Install, StartMenu, Desktop }

    // ---- 布局（96 DPI 的 DIP；运行时乘 DeviceDpi/96）----
    private static readonly Rectangle TitleDip = new(24, 16, 472, 34);
    private static readonly Rectangle SubtitleDip = new(24, 54, 472, 22);
    private static readonly Rectangle DirectoryCardDip = new(24, 88, 472, 96);
    private static readonly Rectangle DirectoryLabelDip = new(44, 104, 432, 18);
    private static readonly Rectangle DirectoryWellDip = new(44, 128, 432, 40);
    private static readonly Rectangle ShortcutCardDip = new(24, 196, 472, 92);
    private static readonly Rectangle StartMenuRowDip = new(44, 210, 432, 28);
    private static readonly Rectangle DesktopRowDip = new(44, 242, 432, 28);
    private static readonly Rectangle InstallDip = new(348, 304, 120, 36);
    private static readonly Rectangle CloseDip = new(256, 304, 84, 36);
    private static readonly Rectangle UninstallDip = new(160, 304, 88, 36);
    private static readonly Rectangle RetryDip = new(76, 304, 76, 36);
    private static readonly Rectangle StatusCardDip = new(24, 352, 472, 76);
    private static readonly Rectangle StatusTextDip = new(42, 364, 436, 56);

    private const string DefaultStatus =
        "安装到用户目录并携带 .NET 桌面运行时，目标机无需预装运行时。\r\n" +
        "不会读取、迁移或修改原三个应用的数据与启动项；卸载会保留用户数据。";

    private readonly TextBox directory;
    private readonly Font titleFont = Palette.Title(15.5f);
    private readonly Font subtitleFont = Palette.Text(9f);
    private readonly Font smallFont = Palette.Text(8.5f);
    private readonly Font bodyFont = Palette.Text(9.5f);

    private bool startMenu;
    private bool desktop;
    private string statusText = DefaultStatus;
    private Color statusColor = Palette.InkSecondary;
    private bool retryVisible;
    private Action? retryAction;

    private Target hover;
    private Target pressed;
    private Target focus;

    public InstallerForm(InstallerOptions options)
    {
        startMenu = options.StartMenu;
        desktop = options.Desktop;

        Text = "工具套件安装程序";
        Font = Palette.Text(9);
        // 自己按 DPI 换算几何，不能让 WinForms 再缩放一遍。
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Palette.Canvas;
        ForeColor = Palette.Ink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        directory = new TextBox
        {
            Text = options.InstallDirectory,
            BorderStyle = BorderStyle.None,
            BackColor = Palette.Well,
            ForeColor = Palette.Ink,
            Font = bodyFont
        };
        Controls.Add(directory);

        ApplyDpi();
        statusText = DefaultStatus;
    }

    private float DpiScale => DeviceDpi / 96f;

    private int S(int dip) => (int)Math.Round(dip * DpiScale);

    private Rectangle D(Rectangle dip) => new(S(dip.X), S(dip.Y), S(dip.Width), S(dip.Height));

    /// <summary>客户区与输入框按当前 DPI 重算；DPI 变化（跨屏拖动）时也走这里。</summary>
    private void ApplyDpi()
    {
        ClientSize = new Size(S(520), S(452));
        var well = D(DirectoryWellDip);
        directory.Height = directory.PreferredHeight;
        directory.Left = well.Left + S(14);
        directory.Width = well.Width - S(28);
        directory.Top = well.Top + (well.Height - directory.Height) / 2;
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpi();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpi();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 路径通常比输入框长：回到开头显示盘符，而不是停在中段。
        directory.SelectionStart = 0;
        directory.SelectionLength = 0;
    }

    protected override void OnPaintBackground(PaintEventArgs e) { /* OnPaint 里整窗清屏 */ }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Palette.Canvas);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Chrome.Text(g, "工具套件安装程序", titleFont, D(TitleDip), Palette.Ink,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);
        Chrome.Text(g, "安装到当前用户目录，不需要管理员权限。", subtitleFont, D(SubtitleDip), Palette.InkSecondary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);

        Chrome.Card(g, D(DirectoryCardDip), S(20), Palette.Card, Color.Empty);
        Chrome.Text(g, "安装目录", smallFont, D(DirectoryLabelDip), Palette.InkSecondary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);
        Chrome.Card(g, D(DirectoryWellDip), S(12), Palette.Well, Palette.Hairline);

        Chrome.Card(g, D(ShortcutCardDip), S(20), Palette.Card, Color.Empty);
        DrawCheckRow(g, Target.StartMenu, StartMenuRowDip, "创建开始菜单快捷方式", startMenu);
        DrawCheckRow(g, Target.Desktop, DesktopRowDip, "创建桌面快捷方式", desktop);

        DrawPill(g, Target.Install, InstallDip, "安装 / 升级", bodyFont, primary: true);
        DrawPill(g, Target.Close, CloseDip, "关闭", subtitleFont, primary: false);
        DrawPill(g, Target.Uninstall, UninstallDip, "卸载", subtitleFont, primary: false);
        if (retryVisible) DrawPill(g, Target.Retry, RetryDip, "重试", subtitleFont, primary: false);

        Chrome.Card(g, D(StatusCardDip), S(20), Palette.Card, Color.Empty);
        Chrome.Text(g, statusText, smallFont, D(StatusTextDip), statusColor,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    private void DrawCheckRow(Graphics g, Target target, Rectangle dip, string text, bool isChecked)
    {
        var row = D(dip);
        var isHover = hover == target;
        if (isHover) Chrome.Card(g, row, S(8), Palette.Hover, Color.Empty);

        var size = S(16);
        var box = new Rectangle(row.Left, row.Top + (row.Height - size) / 2, size, size);
        Chrome.Check(g, box, isChecked, isHover, Palette.Strong);

        var textRect = new Rectangle(box.Right + S(8), row.Top, Math.Max(0, row.Right - box.Right - S(8)), row.Height);
        Chrome.Text(g, text, subtitleFont, textRect, Palette.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (focus == target) DrawRing(g, row);
    }

    private void DrawPill(Graphics g, Target target, Rectangle dip, string text, Font font, bool primary)
    {
        var rect = D(dip);
        var isHover = hover == target;
        var isPressed = pressed == target;
        var fill = primary
            ? (isPressed ? Palette.AccentHover : isHover ? Palette.AccentHover : Palette.Accent)
            : (isHover || isPressed ? Palette.Hover : Color.Empty);
        var stroke = primary ? Color.Empty : (isHover ? Palette.InkSecondary : Palette.Strong);
        var ink = primary ? Color.White : Palette.Ink;
        Chrome.Pill(g, rect, fill, stroke, text, font, ink);
        if (focus == target) DrawRing(g, rect);
    }

    private static void DrawRing(Graphics g, Rectangle rect)
    {
        using var path = Rounded.Path(rect, rect.Height / 2);
        using var pen = new Pen(Palette.FocusRing, 2f);
        g.DrawPath(pen, path);
    }

    // ---- 命中与交互 ----

    private Target HitTest(Point point)
    {
        if (retryVisible && D(RetryDip).Contains(point)) return Target.Retry;
        if (D(InstallDip).Contains(point)) return Target.Install;
        if (D(CloseDip).Contains(point)) return Target.Close;
        if (D(UninstallDip).Contains(point)) return Target.Uninstall;
        if (D(StartMenuRowDip).Contains(point)) return Target.StartMenu;
        if (D(DesktopRowDip).Contains(point)) return Target.Desktop;
        return Target.None;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var next = HitTest(e.Location);
        if (next == hover) return;
        hover = next;
        Cursor = next is Target.None ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover == Target.None && pressed == Target.None) return;
        hover = Target.None;
        pressed = Target.None;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        pressed = HitTest(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var target = HitTest(e.Location);
        var click = pressed;
        pressed = Target.None;
        Invalidate();
        if (click != Target.None && click == target) Activate(target);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // 没有真实按钮，默认按钮 / 取消按钮的语义在这里补齐。
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == Keys.Enter) { Activate(retryVisible ? Target.Retry : Target.Install); return true; }
        if (keyData == Keys.Tab) { CycleFocus(1); return true; }
        if (keyData == (Keys.Shift | Keys.Tab)) { CycleFocus(-1); return true; }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            if (focus is Target.StartMenu or Target.Desktop or Target.Install or Target.Close or Target.Uninstall or Target.Retry)
            {
                Activate(focus);
                e.Handled = true;
            }
        }
    }

    private void CycleFocus(int step)
    {
        var order = new List<Target> { Target.None, Target.StartMenu, Target.Desktop };
        if (retryVisible) order.Add(Target.Retry);
        order.AddRange([Target.Uninstall, Target.Close, Target.Install]);

        var index = order.IndexOf(focus);
        if (index < 0) index = 0;
        var next = order[(index + step + order.Count) % order.Count];
        focus = next;
        if (next == Target.None) directory.Focus(); else Focus();
        Invalidate();
    }

    private void Activate(Target target)
    {
        switch (target)
        {
            case Target.StartMenu:
                startMenu = !startMenu;
                Invalidate();
                break;
            case Target.Desktop:
                desktop = !desktop;
                Invalidate();
                break;
            case Target.Install:
                Run(InstallerMode.Install);
                break;
            case Target.Close:
                Close();
                break;
            case Target.Uninstall:
                ConfirmUninstall();
                break;
            case Target.Retry:
                retryAction?.Invoke();
                break;
        }
    }

    private void ConfirmUninstall()
    {
        var choice = MessageBox.Show(this, "将删除工具套件安装目录与套件自己的注册项，用户数据会保留。继续卸载？",
            "卸载工具套件", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (choice == DialogResult.Yes) Run(InstallerMode.Uninstall);
    }

    private void Run(InstallerMode mode)
    {
        retryVisible = false;
        retryAction = null;
        statusColor = Palette.InkSecondary;
        statusText = mode == InstallerMode.Uninstall ? "正在卸载…" : "正在安装…";
        Invalidate();
        Update();

        try
        {
            var options = new InstallerOptions(mode, false, directory.Text, startMenu, desktop);
            statusText = Program.Execute(options, _ => { });
            if (mode == InstallerMode.Uninstall)
            {
                statusText += "\r\n工具套件已卸载，用户数据保留在 %LOCALAPPDATA%\\"
                              + ToolSuite.Core.SuiteIdentity.DataFolderName + "。";
                Invalidate();
                // 安装目录会在本进程退出后清理，界面立即关闭。
                Close();
                return;
            }
        }
        catch (InstallerBlockedException ex)
        {
            // 明确要求退出后重试，不静默覆盖正在运行的文件。
            statusText = ex.Message + "\r\n退出工具套件后点击“重试”。";
            statusColor = Palette.Danger;
            retryAction = () => Run(mode);
            retryVisible = true;
        }
        catch (Exception ex)
        {
            statusText = (mode == InstallerMode.Uninstall ? "卸载失败：" : "安装失败：") + ex.Message;
            statusColor = Palette.Danger;
        }
        Invalidate();
    }
}
