using Microsoft.Win32;
using System.Windows.Threading;
using Pinto.Views;

namespace Pinto.Services;

public sealed class AppCoordinator : IDisposable
{
    private AppData _state;
    private readonly DataStore _store;
    private readonly ThemeService _theme;
    private readonly Action<string> _report;
    private readonly Dictionary<Guid, NoteWindow> _windows = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _dirty;
    private bool _saveErrorShown;
    private bool _disposed;
    public IReadOnlyList<NoteData> Notes => _state.Notes;
    public ThemePreference ThemePreference => _state.Theme;
    internal IReadOnlyDictionary<Guid, NoteWindow> Windows => _windows;
    internal DataStore Store => _store;


    public AppCoordinator(DataStore store, LoadResult loaded, ThemePreference theme = ThemePreference.Light, Action<string>? report = null)
    {
        _store = store;
        _state = loaded.Data;
        _state.Theme = theme;
        _theme = new(theme);
        _theme.Changed += OnThemeChanged;
        _report = report ?? (_ => { });
        _saveTimer.Tick += (_, _) => Flush();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        if (loaded.IsFirstRun) _state.Notes.Add(new());
        try
        {
            InstallWindows();
            if (loaded.IsFirstRun || loaded.Notice is not null)
            {
                _store.Save(Snapshot());
                _dirty = false;
                _saveTimer.Stop();
            }
            if (loaded.Notice is not null) MessageBox.Show(loaded.Notice, "便签 · 数据恢复", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { Dispose(); throw; }
    }

    public void NewNote()
    {
        if (_state.Notes.Count >= 500) { MessageBox.Show("最多可以创建 500 张便签。", "Pinto"); return; }
        var offset = (_state.Notes.Count % 7) * 26;
        var work = SystemParameters.WorkArea;
        var note = new NoteData();
        note.Left = work.Left + (work.Width - note.Width) / 2 + offset;
        note.Top = work.Top + (work.Height - note.Height) / 2 + offset;
        _state.Notes.Add(note);
        var window = CreateWindow(note);
        window.Show();
        window.FocusNewItem();
        ScheduleSave();
    }
    public void ShowNote(Guid id)
    {
        if (!_windows.TryGetValue(id, out var window)) return;
        var note = _state.Notes.First(note => note.Id == id);
        note.IsVisible = true;
        window.Show();
        window.RestorePlacement();
        window.Activate();
        ScheduleSave();
    }
    public void HideNote(Guid id)
    {
        if (!_windows.TryGetValue(id, out var window)) return;
        window.Model.CommitPendingEdits();
        window.CaptureLayout();
        var note = _state.Notes.First(note => note.Id == id);
        var visible = note.IsVisible;
        note.IsVisible = false;
        ScheduleSave();
        if (!Flush()) { note.IsVisible = visible; return; }
        window.Hide();
    }
    public void ShowAll()
    {
        if (_state.Notes.Count == 0) { NewNote(); return; }
        foreach (var note in _state.Notes) { note.IsVisible = true; var window = _windows[note.Id]; window.Show(); window.RestorePlacement(); }
        _windows.Values.LastOrDefault()?.Activate();
        ScheduleSave();
    }
    public void ActivateVisibleNotes()
    {
        // 重复启动仅唤起已显示的便签，不改变各便签保存的隐藏状态。
        NoteWindow? last = null;
        foreach (var note in _state.Notes.Where(note => note.IsVisible))
        {
            last = _windows[note.Id];
            last.Show();
            last.RestorePlacement();
        }
        last?.Activate();
    }
    public void HideAll()
    {
        Snapshot(commitEdits: true);
        var visibility = _state.Notes.ToDictionary(note => note.Id, note => note.IsVisible);
        foreach (var note in _state.Notes) note.IsVisible = false;
        ScheduleSave();
        if (!Flush())
        {
            foreach (var note in _state.Notes) note.IsVisible = visibility[note.Id];
            return;
        }
        foreach (var window in _windows.Values) window.Hide();
    }
    public void DeleteNote(Guid id)
    {
        if (!_windows.TryGetValue(id, out var window)) return;
        if (MessageBox.Show(window, $"删除“{window.Model.Title}”及其全部清单项？此操作无法撤销。", "删除便签", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _state.Notes.RemoveAll(note => note.Id == id);
        _windows.Remove(id);
        window.ForceClose();
        ScheduleSave();
        Flush();
    }
    public void SetTheme(ThemePreference preference)
    {
        _state.Theme = preference;
        _theme.Apply(preference);
        ScheduleSave();
    }
    public void ScheduleSave()
    {
        if (_disposed) return;
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }
    public AppData Snapshot(bool commitEdits = false)
    {
        foreach (var window in _windows.Values) { if (commitEdits) window.Model.CommitPendingEdits(); window.CaptureLayout(); window.Model.Snapshot(); }
        return _state;
    }
    public bool Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return true;
        try
        {
            _store.Save(Snapshot());
            _dirty = false;
            _saveErrorShown = false;
            _saveTimer.Stop();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
            return true;
        }
        catch (Exception error) when (IsExpectedError(error))
        {
            _dirty = true;
            _saveTimer.Interval = TimeSpan.FromSeconds(5);
            _saveTimer.Start();
            if (!_saveErrorShown)
            {
                _saveErrorShown = true;
                _report($"自动保存失败，当前编辑仍保留，请检查磁盘或导出备份。{error.Message} 数据目录：{_store.DirectoryPath}");
            }
            return false;
        }
    }
    public void ExportBackup()
    {
        var dialog = new SaveFileDialog { Title = "导出全部便签", Filter = "Pinto 备份 (*.json)|*.json", FileName = $"Pinto备份-{DateTime.Now:yyyyMMdd-HHmmss}.json", AddExtension = true, DefaultExt = ".json" };
        if (dialog.ShowDialog() != true) return;
        try { _store.Export(Snapshot(commitEdits: true), dialog.FileName); MessageBox.Show("便签备份已导出。", "便签备份", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception error) when (IsExpectedError(error)) { ShowError("导出失败", error); }
    }
    public void ImportBackup()
    {
        var file = new OpenFileDialog { Title = "选择 Pinto 备份", Filter = "Pinto 备份 (*.json)|*.json", CheckFileExists = true };
        if (file.ShowDialog() != true) return;
        try
        {
            var incoming = _store.ReadBackup(file.FileName);
            var choice = new ImportDialog(incoming.Notes.Count, _state.Notes.Count);
            _theme.ApplyResources(choice);
            if (_windows.Values.FirstOrDefault(window => window.IsVisible) is { } owner) choice.Owner = owner;
            if (choice.ShowDialog() != true || choice.SelectedMode is not { } mode) return;
            ApplyImport(file.FileName, mode);
        }
        catch (Exception error) when (IsExpectedError(error)) { ShowError("导入失败，当前便签未被替换", error); }
    }
    internal ImportResult ApplyImport(string file, ImportMode mode)
    {
        var result = _store.Import(Snapshot(commitEdits: true), file, mode);
        _saveTimer.Stop();
        _dirty = false;
        foreach (var window in _windows.Values) window.ForceClose();
        _windows.Clear();
        _state = result.Data;
        InstallWindows();

        if (result.SafetyBackupPath is not null)
            MessageBox.Show($"替换已完成。替换前的便签已备份至：\n\n{result.SafetyBackupPath}", "Pinto · 自动备份", MessageBoxButton.OK, MessageBoxImage.Information);
        return result;
    }
    public void PrepareToStop()
    {
        _saveTimer.Stop();
        _store.Save(Snapshot(commitEdits: true));
        _dirty = false;
    }
    public void ApplyResources(FrameworkElement target) => _theme.ApplyResources(target);
    private void InstallWindows()
    {
        foreach (var note in _state.Notes)
        {
            var window = CreateWindow(note);
            window.ShowActivated = false;
            if (note.IsVisible) window.Show();
        }
    }
    private NoteWindow CreateWindow(NoteData note)
    {
        var window = new NoteWindow(note, this);
        _windows.Add(note.Id, window);
        return window;
    }
    private void OnThemeChanged()
    {
        foreach (var window in _windows.Values) { _theme.ApplyResources(window); window.ApplyTheme(); }
        foreach (var dialog in Application.Current.Windows.OfType<ImportDialog>()) _theme.ApplyResources(dialog);
    }
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Application.Current.Dispatcher.BeginInvoke(() => { foreach (var window in _windows.Values.Where(window => window.IsVisible)) window.RestorePlacement(); });
    private static bool IsExpectedError(Exception error) => error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Security.SecurityException or InvalidOperationException or FormatException;
    private static void ShowError(string title, Exception error) => MessageBox.Show(error.Message, "Pinto · " + title, MessageBoxButton.OK, MessageBoxImage.Warning);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _theme.Changed -= OnThemeChanged;
        _theme.Dispose();

        foreach (var window in _windows.Values) window.ForceClose();
        _windows.Clear();
    }
}





