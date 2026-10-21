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
        if (_state.Notes.FirstOrDefault(note => note.Id == id) is not { } note) return;
        note.IsVisible = true;
        // 隐藏时窗口已释放：按需重建视图模型与窗口，再恢复内容与布局。
        var window = EnsureWindow(note);
        window.Show();
        window.RestorePlacement();
        window.Activate();
        ScheduleSave();
    }
    public void HideNote(Guid id)
    {
        // 关闭事件可能排到停用之后执行；已释放时不再写盘或访问窗口。
        if (_disposed) return;
        if (_state.Notes.FirstOrDefault(note => note.Id == id) is not { } note) return;
        if (_windows.TryGetValue(id, out var window))
        {
            // 先在窗口仍存活时提交编辑并捕获布局，保存成功后才回收窗口。
            window.Model.CommitPendingEdits();
            window.CaptureLayout();
        }
        var visible = note.IsVisible;
        note.IsVisible = false;
        ScheduleSave();
        if (!Flush())
        {
            // 保存失败：保留窗口、视图模型与未提交草稿，数据不丢，修复后可再次隐藏。
            note.IsVisible = visible;
            return;
        }
        if (window is not null) ReleaseWindow(window);
    }
    public void ShowAll()
    {
        if (_state.Notes.Count == 0) { NewNote(); return; }
        NoteWindow? last = null;
        foreach (var note in _state.Notes)
        {
            note.IsVisible = true;
            last = EnsureWindow(note);
            last.Show();
            last.RestorePlacement();
        }
        last?.Activate();
        ScheduleSave();
    }
    public void ActivateVisibleNotes()
    {
        // 重复启动仅唤起已显示的便签，不改变各便签保存的隐藏状态。
        NoteWindow? last = null;
        foreach (var note in _state.Notes.Where(note => note.IsVisible))
        {
            last = EnsureWindow(note);
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
        foreach (var window in _windows.Values.ToArray()) ReleaseWindow(window);
    }
    public void DeleteNote(Guid id)
    {
        if (_state.Notes.FirstOrDefault(note => note.Id == id) is not { } note) return;
        _windows.TryGetValue(id, out var window);
        var title = window?.Model.Title ?? note.Title;
        var owner = window ?? _windows.Values.FirstOrDefault(candidate => candidate.IsVisible);
        var result = owner is null
            ? MessageBox.Show($"删除“{title}”及其全部清单项？此操作无法撤销。", "删除便签", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, $"删除“{title}”及其全部清单项？此操作无法撤销。", "删除便签", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;
        _state.Notes.RemoveAll(candidate => candidate.Id == id);
        _windows.Remove(id);
        window?.ForceClose();
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
        // 只为已显示便签建窗；隐藏便签保持无窗口，显示时再重建。
        foreach (var note in _state.Notes.Where(note => note.IsVisible))
        {
            var window = EnsureWindow(note);
            window.Show();
        }
    }
    private NoteWindow EnsureWindow(NoteData note)
    {
        if (_windows.TryGetValue(note.Id, out var existing)) return existing;
        var window = CreateWindow(note);
        window.ShowActivated = false;
        return window;
    }
    private NoteWindow CreateWindow(NoteData note)
    {
        var window = new NoteWindow(note, this);
        _windows.Add(note.Id, window);
        return window;
    }
    /// <summary>隐藏保存成功后回收窗口：先移出索引再关闭，避免关闭路径再次触发隐藏。</summary>
    private void ReleaseWindow(NoteWindow window)
    {
        _windows.Remove(window.Model.Id);
        window.Release();
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





