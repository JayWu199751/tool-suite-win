using Pinto.Services;
using ToolSuite.Core;
using SuiteTheme = ToolSuite.Core.ThemePreference;

namespace Pinto;

public sealed class PintoModule : IToolModule
{
    private AppCoordinator? _coordinator;
    private ModuleContext? _context;
    public void Configure(ModuleContext context) => _context = context;
    public string Id => "pinto";
    public string DisplayName => "便签";
    public IReadOnlyList<ToolCommand> Commands
    {
        get
        {
            if (_coordinator is not { } coordinator)
                return _context is null ? [] : [new ToolCommand("recover", "从备份恢复损坏数据…", token => { token.ThrowIfCancellationRequested(); RecoverBackup(); return Task.CompletedTask; }, RequiresRunning: false)];
            List<ToolCommand> commands =
            [
                Command("new", "新建便签", coordinator.NewNote),
                Command("show-all", "显示全部便签", coordinator.ShowAll),
                Command("hide-all", "隐藏全部便签", coordinator.HideAll),
                Command("export", "导出便签备份…", coordinator.ExportBackup),
                Command("import", "导入便签备份…", coordinator.ImportBackup)
            ];
            foreach (var note in coordinator.Notes.Where(note => !note.IsVisible))
            {
                var id = note.Id;
                commands.Add(Command($"show-note/{id}", note.Title, () => coordinator.ShowNote(id)));
            }
            return commands;
        }
    }
    private static ToolCommand Command(string id, string label, Action action)
        => new(id, label, token => { token.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; });
    public Task StartAsync(ModuleContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Configure(context);
        if (_coordinator is not null) return Task.CompletedTask;
        var store = new DataStore(context.DataDirectory);
        _coordinator = new AppCoordinator(store, store.Load(), MapTheme(context.Theme), context.ReportError);
        return Task.CompletedTask;
    }
    public Task PrepareToStopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _coordinator?.PrepareToStop();
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _coordinator?.Dispose();
        _coordinator = null;
        return Task.CompletedTask;
    }
    public void ApplyTheme(SuiteTheme theme)
    {
        if (_context is not null) _context = _context with { Theme = theme };
        _coordinator?.SetTheme(MapTheme(theme));
    }
    private void RecoverBackup()
    {
        if (_context is not { } context) return;
        if (MessageBox.Show("将使用所选备份恢复套件便签。当前数据文件会保留副本，工具将保持停用，恢复后请重新启用。是否继续？", "便签数据恢复", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var file = new Microsoft.Win32.OpenFileDialog { Title = "选择恢复备份", Filter = "Pinto 备份 (*.json)|*.json", CheckFileExists = true };
        if (file.ShowDialog() != true) return;
        try
        {
            new DataStore(context.DataDirectory).RecoverFromBackup(file.FileName);
            MessageBox.Show("便签备份已恢复，请重新启用便签。", "便签数据恢复", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Security.SecurityException)
        {
            context.ReportError("恢复失败，原文件已保留：" + error.Message);
        }
    }
    private static Core.ThemePreference MapTheme(SuiteTheme theme) => theme == SuiteTheme.Dark ? Core.ThemePreference.Dark : Core.ThemePreference.Light;
}




