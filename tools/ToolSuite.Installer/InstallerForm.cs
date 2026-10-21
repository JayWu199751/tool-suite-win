namespace ToolSuite.Installer;

/// <summary>安装 / 卸载对话框。视觉语言见 docs/design-system.md：
/// 浅色画布 (#F5F5F7)、白色 20 DIP 圆角卡片、主操作蓝色胶囊、次操作幽灵胶囊、零投影。</summary>
internal sealed class InstallerForm : Form
{
    private readonly TextBox directory;
    private readonly SuiteCheckBox startMenu = new() { Text = "创建开始菜单快捷方式", Checked = true };
    private readonly SuiteCheckBox desktop = new() { Text = "创建桌面快捷方式" };
    private readonly Label status = new()
    {
        AutoSize = false, BorderStyle = BorderStyle.None,
        BackColor = Palette.Card, ForeColor = Palette.InkSecondary,
        Font = Palette.Text(8.5f), Padding = new Padding(0), UseMnemonic = false
    };
    private readonly PillButton retry = new()
    {
        Text = "重试", Size = new Size(76, 36), Visible = false,
        Fill = Color.Empty, Stroke = Palette.Strong, TextColor = Palette.Ink,
        HoverFill = Palette.Hover, HoverStroke = Palette.InkSecondary, Font = Palette.Text(9)
    };
    private Action? retryAction;

    public InstallerForm(InstallerOptions options)
    {
        Text = "工具套件安装程序";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Palette.Text(9);
        ClientSize = new Size(520, 452);
        BackColor = Palette.Canvas;
        ForeColor = Palette.Ink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        Controls.Add(Text_("工具套件安装程序", 15.5f, 24, 18, 472, 30, Palette.Ink, Palette.Canvas, semibold: true));
        Controls.Add(Text_("安装到当前用户目录，不需要管理员权限。", 9f, 24, 52, 472, 20, Palette.InkSecondary, Palette.Canvas));

        var locationCard = Card(88, 96);
        locationCard.Controls.Add(Text_("安装目录", 8.5f, 20, 14, 432, 18, Palette.InkSecondary));
        var well = new CardPanel
        {
            Location = new Point(20, 38), Size = new Size(432, 38),
            CornerRadius = 12, Fill = Palette.Well, Stroke = Palette.Hairline
        };
        directory = new TextBox
        {
            Text = options.InstallDirectory,
            BorderStyle = BorderStyle.None,
            BackColor = Palette.Well,
            ForeColor = Palette.Ink,
            Font = Palette.Text(9.5f),
            Location = new Point(14, 10),
            Width = 404
        };
        well.Controls.Add(directory);
        locationCard.Controls.Add(well);
        Controls.Add(locationCard);

        var shortcutCard = Card(196, 92);
        startMenu.Location = new Point(20, 18);
        startMenu.Width = 432;
        desktop.Location = new Point(20, 50);
        desktop.Width = 432;
        shortcutCard.Controls.Add(startMenu);
        shortcutCard.Controls.Add(desktop);
        Controls.Add(shortcutCard);

        var install = new PillButton
        {
            Text = "安装 / 升级", Location = new Point(348, 304), Size = new Size(120, 36),
            Font = Palette.Text(9.5f)
        };
        install.Click += (_, _) => Run(InstallerMode.Install);
        var close = Ghost("关闭", 256, 304, 84);
        close.Click += (_, _) => Close();
        var uninstall = Ghost("卸载", 160, 304, 88);
        uninstall.Click += (_, _) => ConfirmUninstall();
        retry.Location = new Point(76, 304);
        retry.Click += (_, _) => retryAction?.Invoke();
        Controls.Add(install);
        Controls.Add(close);
        Controls.Add(uninstall);
        Controls.Add(retry);

        var statusCard = Card(352, 76);
        status.Location = new Point(18, 14);
        status.Size = new Size(436, 48);
        statusCard.Controls.Add(status);
        Controls.Add(statusCard);

        status.Text = "安装到用户目录并携带 .NET 桌面运行时，目标机无需预装运行时。\r\n" +
                      "不会读取、迁移或修改原三个应用的数据与启动项；卸载会保留用户数据。";
    }

    /// <summary>固定矩形标签：AutoSize 关闭时 Label 会按宽度换行。</summary>
    private static Label Text_(string text, float size, int left, int top, int width, int height, Color color,
        Color? back = null, bool semibold = false) => new()
    {
        Text = text,
        Location = new Point(left, top),
        Size = new Size(width, height),
        AutoSize = false,
        BackColor = back ?? Palette.Card,
        ForeColor = color,
        Font = semibold ? Palette.Title(size) : Palette.Text(size),
        UseMnemonic = false
    };

    private static CardPanel Card(int top, int height) => new()
    {
        Location = new Point(24, top),
        Size = new Size(472, height),
        CornerRadius = 20,
        Fill = Palette.Card
    };

    private static PillButton Ghost(string text, int left, int top, int width) => new()
    {
        Text = text, Location = new Point(left, top), Size = new Size(width, 36),
        Fill = Color.Empty, Stroke = Palette.Strong, TextColor = Palette.Ink,
        HoverFill = Palette.Hover, HoverStroke = Palette.InkSecondary, Font = Palette.Text(9)
    };

    private void ConfirmUninstall()
    {
        var choice = MessageBox.Show(this, "将删除工具套件安装目录与套件自己的注册项，用户数据会保留。继续卸载？",
            "卸载工具套件", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (choice == DialogResult.Yes) Run(InstallerMode.Uninstall);
    }

    private void Run(InstallerMode mode)
    {
        retry.Visible = false;
        retryAction = null;
        status.ForeColor = Palette.InkSecondary;
        try
        {
            var options = new InstallerOptions(mode, false, directory.Text, startMenu.Checked, desktop.Checked);
            status.Text = Program.Execute(options, _ => { });
            if (mode == InstallerMode.Uninstall)
            {
                status.Text += "\r\n工具套件已卸载，用户数据保留在 %LOCALAPPDATA%\\" + ToolSuite.Core.SuiteIdentity.DataFolderName + "。";
                // 安装目录会在本进程退出后清理，界面立即关闭。
                Close();
            }
        }
        catch (InstallerBlockedException ex)
        {
            // 明确要求退出后重试，不静默覆盖正在运行的文件。
            status.Text = ex.Message + "\r\n退出工具套件后点击“重试”。";
            status.ForeColor = Palette.Danger;
            retryAction = () => Run(mode);
            retry.Visible = true;
        }
        catch (Exception ex)
        {
            status.Text = (mode == InstallerMode.Uninstall ? "卸载失败：" : "安装失败：") + ex.Message;
            status.ForeColor = Palette.Danger;
        }
    }
}
