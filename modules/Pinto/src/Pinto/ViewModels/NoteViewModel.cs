using System.Collections.ObjectModel;
using Pinto.Services;

namespace Pinto.ViewModels;

public sealed class ChecklistItemViewModel : ObservableObject
{
    private readonly ChecklistItemData _data;
    private readonly Action _save;
    private readonly Action<ChecklistItemViewModel> _remove;
    private bool _isEditing;
    private string _draft = "";

    public ChecklistItemViewModel(ChecklistItemData data, Action save, Action<ChecklistItemViewModel> remove) { _data = data; _save = save; _remove = remove; }
    public Guid Id => _data.Id;
    public string Text
    {
        get => _data.Text;
        private set { if (_data.Text == value) return; _data.Text = value; Changed(); _save(); }
    }
    public bool IsCompleted
    {
        get => _data.IsCompleted;
        set { if (_data.IsCompleted == value) return; _data.IsCompleted = value; Changed(); _save(); }
    }
    public bool IsEditing { get => _isEditing; private set => Set(ref _isEditing, value); }
    public string Draft { get => _draft; set => Set(ref _draft, value); }
    public void BeginEdit() { Draft = Text; IsEditing = true; }
    public void CommitEdit()
    {
        if (!IsEditing) return;
        var text = Draft.Trim();
        // 先结束编辑，避免控件移除或失焦时重入提交。
        IsEditing = false;
        if (text.Length == 0) { _remove(this); return; }
        Text = text;
    }
    public void CancelEdit() => IsEditing = false;
    public ChecklistItemData Snapshot() => new() { Id = Id, Text = Text, IsCompleted = IsCompleted };
}

public sealed class NoteViewModel : ObservableObject
{
    private readonly NoteData _data;
    private readonly Action _save;
    private string _newItemText = "";
    private bool _isTitleEditing;
    private string _titleDraft = "";

    public NoteViewModel(NoteData data, Action save)
    {
        _data = data;
        _save = save;
        Items = new(data.Items.Select(item => new ChecklistItemViewModel(item, save, RemoveItem)));
    }
    public Guid Id => _data.Id;
    public string Title => string.IsNullOrWhiteSpace(_data.Title) ? "新便签" : _data.Title;
    public AccentColor Accent
    {
        get => _data.Accent;
        set { if (_data.Accent == value) return; _data.Accent = value; Changed(); RefreshAccent(); _save(); }
    }
    public Brush AccentBrush => ThemeService.GetAccentBrush(Accent);
    public Brush CheckmarkBrush => SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : ThemeService.IsDark ? Brushes.Black : Brushes.White;
    public System.Collections.ObjectModel.ObservableCollection<ChecklistItemViewModel> Items { get; }
    public string NewItemText { get => _newItemText; set => Set(ref _newItemText, value); }
    public bool IsEmpty => Items.Count == 0;
    public bool IsTitleEditing { get => _isTitleEditing; private set => Set(ref _isTitleEditing, value); }
    public string TitleDraft { get => _titleDraft; set => Set(ref _titleDraft, value); }

    public bool AddItem()
    {
        var text = NewItemText.Trim();
        if (text.Length == 0) return false;
        if (Items.Count >= 10000) { MessageBox.Show("每张便签最多保存 10000 条清单项。", "Pinto"); return false; }
        Items.Add(new(new ChecklistItemData { Text = text }, _save, RemoveItem));
        NewItemText = "";
        Changed(nameof(IsEmpty));
        _save();
        return true;
    }
    public void RemoveItem(ChecklistItemViewModel item)
    {
        if (!Items.Remove(item)) return;
        Changed(nameof(IsEmpty));
        _save();
    }
    public void MoveItem(ChecklistItemViewModel item, int destination)
    {
        var source = Items.IndexOf(item);
        if (source < 0 || destination < 0 || destination >= Items.Count || source == destination) return;
        Items.Move(source, destination);
        _save();
    }
    public void BeginTitleEdit() { TitleDraft = Title; IsTitleEditing = true; }
    public void CommitTitleEdit()
    {
        _data.Title = string.IsNullOrWhiteSpace(TitleDraft) ? "新便签" : TitleDraft.Trim();
        IsTitleEditing = false;
        Changed(nameof(Title));
        _save();
    }
    public void CancelTitleEdit() => IsTitleEditing = false;
    public void CommitPendingEdits()
    {
        AddItem(); // 隐藏或停用时提交新增输入，窗口释放后仍可恢复。
        if (IsTitleEditing) CommitTitleEdit();
        foreach (var item in Items.Where(item => item.IsEditing).ToArray()) item.CommitEdit();
    }
    public void RefreshAccent() { Changed(nameof(AccentBrush)); Changed(nameof(CheckmarkBrush)); }
    public NoteData Snapshot()
    {
        _data.Items = Items.Select(item => item.Snapshot()).ToList();
        return _data;
    }
}

