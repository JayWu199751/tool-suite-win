namespace ToolSuite.Installer;

internal sealed class InstallerForm : Form
{
    private readonly TextBox directory;
    private readonly CheckBox startMenu = new() { Text = "创建开始菜单快捷方式", Checked = true, AutoSize = true };
    private readonly CheckBox desktop = new() { Text = "创建桌面快捷方式", AutoSize = true };
    private readonly Label status = new()
    {
        AutoSize = false, BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.TopLeft,
        Padding = new Padding(8), Location = new Point(16, 190), Size = new Size(448, 120)
    };
    private readonly Button retry = new() { Text = "重试", Location = new Point(264, 148), Size = new Size(104, 30), Visible = false };
    private Action? retryAction;

    public InstallerForm(InstallerOptions options)
    {
        Text = "工具套件安装程序";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(480, 326);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        directory = new TextBox { Text = options.InstallDirectory, Location = new Point(16, 40), Width = 448 };
        Controls.Add(new Label { Text = "安装目录（当前用户，无需管理员权限）", Location = new Point(16, 16), AutoSize = true });
        Controls.Add(directory);
        Controls.Add(startMenu);
        startMenu.Location = new Point(16, 74);
        Controls.Add(desktop);
        desktop.Location = new Point(16, 98);

        var install = new Button { Text = "安装 / 升级", Location = new Point(16, 148), Size = new Size(120, 30) };
        install.Click += (_, _) => Run(InstallerMode.Install);
        var uninstall = new Button { Text = "卸载", Location = new Point(144, 148), Size = new Size(104, 30) };
        uninstall.Click += (_, _) => ConfirmUninstall();
        retry.Click += (_, _) => retryAction?.Invoke();
        var close = new Button { Text = "关闭", Location = new Point(376, 148), Size = new Size(88, 30) };
        close.Click += (_, _) => Close();
        Controls.Add(install);
        Controls.Add(uninstall);
        Controls.Add(retry);
        Controls.Add(close);
        Controls.Add(status);
        status.Text = "安装到用户目录并携带 .NET 桌面运行时，目标机无需预装运行时。\r\n" +
                      "不会读取、迁移或修改原三个应用的数据与启动项；卸载会保留用户数据。";
    }

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
        try
        {
            var options = new InstallerOptions(mode, false, directory.Text, startMenu.Checked, desktop.Checked);
            status.Text = Program.Execute(options, _ => { });
            if (mode == InstallerMode.Uninstall)
            {
                status.Text += "\r\n工具套件已卸载，用户数据保留在 %LOCALAPPDATA%\\ToolSuite。";
                // 安装目录会在本进程退出后清理，界面立即关闭。
                Close();
            }
        }
        catch (InstallerBlockedException ex)
        {
            // 明确要求退出后重试，不静默覆盖正在运行的文件。
            status.Text = ex.Message + "\r\n退出工具套件后点击“重试”。";
            retryAction = () => Run(mode);
            retry.Visible = true;
        }
        catch (Exception ex)
        {
            status.Text = (mode == InstallerMode.Uninstall ? "卸载失败：" : "安装失败：") + ex.Message;
        }
    }
}
