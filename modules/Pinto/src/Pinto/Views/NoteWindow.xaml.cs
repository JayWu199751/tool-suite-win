using System.Windows.Threading;
using System.Windows.Interop;
using Pinto.Services;
using Pinto.ViewModels;

namespace Pinto.Views;

public partial class NoteWindow : Window
{
    private readonly AppCoordinator _coordinator;
    private readonly NoteData _data;
    private HwndSource? _source;
    private bool _allowClose;
    private bool _ready;
    private bool _interactivePlacement;
    private bool _applyingPlacement;
    private Point _dragStart;
    private ChecklistItemViewModel? _dragItem;
    private Border? _dropRow;
    public NoteViewModel Model { get; }
    internal bool HasBackdrop { get; private set; }
    internal DockEdges DockedEdges => _data.DockEdges;
    private string DragFormat => $"Pinto.ChecklistItem.{Model.Id}";

    public NoteWindow(NoteData data, AppCoordinator coordinator)
    {
        _coordinator = coordinator;
        _data = data;
        Model = new(data, coordinator.ScheduleSave);
        _coordinator.ApplyResources(this);
        InitializeComponent();
        DataContext = Model;
        Model.PropertyChanged += OnModelPropertyChanged;
        Width = Math.Clamp(data.Width, MinWidth, 1600);
        Height = Math.Clamp(data.Height, MinHeight, 2000);
        var work = SystemParameters.WorkArea;
        Left = data.Left.HasValue ? Math.Clamp(data.Left.Value, -100000, 100000) : work.Left + (work.Width - Width) / 2;
        Top = data.Top.HasValue ? Math.Clamp(data.Top.Value, -100000, 100000) : work.Top + (work.Height - Height) / 2;
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(PlacementMessage);
            NativeWindow.EnsureTrayOnly(this);
            ApplyTheme();
        };
        Loaded += (_, _) => { NativeWindow.EnsureTrayOnly(this); RestorePlacement(); _ready = true; CaptureLayout(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) NativeWindow.EnsureTrayOnly(this); };
        LocationChanged += (_, _) => CaptureLayout();
        SizeChanged += (_, _) =>
        {
            if (_ready && !_interactivePlacement && _data.DockEdges != DockEdges.None) RestorePlacement();
            CaptureLayout();
        };
        Closing += OnClosing;
        Closed += OnClosed;
        ContextMenuOpening += Window_ContextMenuOpening;
        ContextMenu = new ContextMenu();
    }

    public void ApplyTheme()
    {
        Model.RefreshAccent();
        HasBackdrop = NativeWindow.ApplyBackdrop(this);
        Surface.SetResourceReference(Border.BackgroundProperty, HasBackdrop || AllowsTransparency ? "SurfaceBrush" : "SolidSurfaceBrush");
    }

    public void CaptureLayout()
    {
        if (!_ready || WindowState != WindowState.Normal) return;
        if (_data.Left == Left && _data.Top == Top && _data.Width == Width && _data.Height == Height) return;
        _data.Left = Left;
        _data.Top = Top;
        _data.Width = Width;
        _data.Height = Height;
        _coordinator.ScheduleSave();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteViewModel.IsEmpty) && Model.IsEmpty)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ItemsScroller.ScrollToHome());
    }

    public void RestorePlacement()
    {
        if (_applyingPlacement || _interactivePlacement) return;
        _applyingPlacement = true;
        try { NativeWindow.EnsureOnScreen(this, _data.DockEdges); }
        finally { _applyingPlacement = false; }
        CaptureLayout();
    }

    internal void SetDockEdges(DockEdges edges)
    {
        if (!EdgeDocking.IsValid(edges)) throw new ArgumentOutOfRangeException(nameof(edges));
        _data.DockEdges = edges;
        RestorePlacement();
        _coordinator.ScheduleSave();
        _coordinator.Flush();
    }

    private nint PlacementMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0231) _interactivePlacement = true; // WM_ENTERSIZEMOVE
        else if (message is 0x0216 or 0x0214) // WM_MOVING / WM_SIZING，坐标为物理像素。
        {
            if (NativeWindow.SnapMovingBounds(this, lParam, message == 0x0214, _coordinator.Windows.Values) is { } edges)
            {
                if (_data.DockEdges != edges) { _data.DockEdges = edges; _coordinator.ScheduleSave(); }
                handled = true;
                return 1;
            }
        }
        else if (message == 0x001A && wParam == 0x002F) // WM_SETTINGCHANGE / SPI_SETWORKAREA
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RestorePlacement);
        else if (message == 0x0232) // WM_EXITSIZEMOVE
        {
            _interactivePlacement = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!_ready || !IsVisible) return;
                RestorePlacement();
                _coordinator.ScheduleSave();
                _coordinator.Flush();
            });
        }
        return 0;
    }

    public void ForceClose() { _allowClose = true; Close(); }
    /// <summary>隐藏保存成功后的回收入口：释放原生钩子、视图模型与菜单引用后关闭窗口。</summary>
    internal void Release()
    {
        _allowClose = true;
        Close();
        DataContext = null;
        ContextMenu = null;
    }
    public void FocusNewItem() { Activate(); NewItemEditor.Focus(); }
    private void OnClosed(object? sender, EventArgs e)
    {
        // 窗口关闭即销毁 HWND；显式摘钩并断开视图模型，隐藏期间不保留窗口侧订阅。
        _source?.RemoveHook(PlacementMessage);
        _source = null;
        Model.PropertyChanged -= OnModelPropertyChanged;
        DataContext = null;
        ContextMenu = null;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        // 在 Closing 回调里直接回收会重入 Close；把隐藏/按需释放排到本次关闭事件返回之后。
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => _coordinator.HideNote(Model.Id)));
    }
    private void Hide_Click(object sender, RoutedEventArgs e) => _coordinator.HideNote(Model.Id);
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            _coordinator.HideNote(Model.Id);
        }
        else if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null || FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null) return;
        if (e.ClickCount == 2) Model.BeginTitleEdit();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        e.Handled = true;
    }
    private void NewItem_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !IsImeComposing(e)) { AddItem(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Model.NewItemText = ""; e.Handled = true; }
    }
    private static bool IsImeComposing(KeyEventArgs e) => e.ImeProcessedKey != Key.None;
    private void Add_Click(object sender, RoutedEventArgs e) => AddItem();
    private void AddItem()
    {
        if (Model.AddItem()) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ItemsScroller.ScrollToEnd());
        NewItemEditor.Focus();
    }
    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox editor) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (editor.IsVisible) { editor.Focus(); editor.SelectAll(); } });
    }
    private void TitleEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !IsImeComposing(e)) { Model.CommitTitleEdit(); NewItemEditor.Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Model.CancelTitleEdit(); NewItemEditor.Focus(); e.Handled = true; }
    }
    private void TitleEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Model.IsTitleEditing) Model.CommitTitleEdit();
    }
    private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null) return;
        if (sender is FrameworkElement { DataContext: ChecklistItemViewModel item }) { item.BeginEdit(); e.Handled = true; }
    }
    private void ItemEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChecklistItemViewModel item }) return;
        if (e.Key == Key.Enter && !IsImeComposing(e)) { item.CommitEdit(); NewItemEditor.Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { item.CancelEdit(); NewItemEditor.Focus(); e.Handled = true; }
    }
    private void ItemEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChecklistItemViewModel item } && item.IsEditing) item.CommitEdit();
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
            && FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is null
            && FindRow(e.OriginalSource as DependencyObject) is { DataContext: ChecklistItemViewModel menuItem } row)
        {
            OpenItemMenu(row, menuItem, true);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { _coordinator.NewNote(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W) { _coordinator.HideNote(Model.Id); e.Handled = true; }
        else if (e.Key == Key.F2 && FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is null) { Model.BeginTitleEdit(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && (e.Key == Key.Up || e.Key == Key.Down || e.SystemKey == Key.Up || e.SystemKey == Key.Down))
        {
            var item = FindItem(e.OriginalSource as DependencyObject);
            if (item is null) return;
            var up = e.Key == Key.Up || e.SystemKey == Key.Up;
            Model.MoveItem(item, Model.Items.IndexOf(item) + (up ? -1 : 1));
            e.Handled = true;
        }
    }

    private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.Handled || FindItem(e.OriginalSource as DependencyObject) is not null || FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null) return;
        RebuildContextMenu();
    }
    internal ContextMenu RebuildContextMenu()
    {
        _coordinator.ApplyResources(ContextMenu!);
        ContextMenu!.Items.Clear();
        ContextMenu.Items.Add(Menu("新建便签", () => _coordinator.NewNote()));
        ContextMenu.Items.Add(Menu("修改标题", Model.BeginTitleEdit));
        var colors = new MenuItem { Header = "强调色" };
        foreach (var accent in Enum.GetValues<AccentColor>())
        {
            var choice = Menu(ThemeService.AccentName(accent), () => Model.Accent = accent);
            choice.IsCheckable = true;
            choice.IsChecked = Model.Accent == accent;
            colors.Items.Add(choice);
        }
        ContextMenu.Items.Add(colors);
        var docking = new MenuItem { Header = "贴边固定" };
        foreach (var (label, edges) in new[]
        {
            ("左边", DockEdges.Left), ("右边", DockEdges.Right), ("上边", DockEdges.Top), ("下边", DockEdges.Bottom),
            ("左上角", DockEdges.Left | DockEdges.Top), ("右上角", DockEdges.Right | DockEdges.Top),
            ("左下角", DockEdges.Left | DockEdges.Bottom), ("右下角", DockEdges.Right | DockEdges.Bottom)
        })
        {
            var choice = Menu(label, () => SetDockEdges(edges));
            choice.IsCheckable = true;
            choice.IsChecked = _data.DockEdges == edges;
            docking.Items.Add(choice);
        }
        docking.Items.Add(new Separator());
        var undock = Menu("取消贴边", () => SetDockEdges(DockEdges.None));
        undock.IsEnabled = _data.DockEdges != DockEdges.None;
        docking.Items.Add(undock);
        ContextMenu.Items.Add(docking);
        ContextMenu.Items.Add(new Separator());
        ContextMenu.Items.Add(Menu("隐藏便签", () => _coordinator.HideNote(Model.Id)));
        ContextMenu.Items.Add(Menu("删除便签…", () => _coordinator.DeleteNote(Model.Id)));
        return ContextMenu;
    }
    private void Item_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null) return;
        if (sender is not FrameworkElement { DataContext: ChecklistItemViewModel item } row) return;
        e.Handled = true;
        OpenItemMenu(row, item, false);
    }
    private void OpenItemMenu(FrameworkElement row, ChecklistItemViewModel item, bool fromKeyboard)
    {
        var menu = new ContextMenu { PlacementTarget = row };
        _coordinator.ApplyResources(menu);
        if (fromKeyboard) menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Items.Add(Menu(item.IsCompleted ? "标记未完成" : "标记完成", () => item.IsCompleted = !item.IsCompleted));
        menu.Items.Add(Menu("编辑", item.BeginEdit));
        var index = Model.Items.IndexOf(item);
        var up = Menu("上移", () => Model.MoveItem(item, Model.Items.IndexOf(item) - 1)); up.IsEnabled = index > 0;
        var down = Menu("下移", () => Model.MoveItem(item, Model.Items.IndexOf(item) + 1)); down.IsEnabled = index < Model.Items.Count - 1;
        menu.Items.Add(up); menu.Items.Add(down);
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("删除清单项", () => Model.RemoveItem(item)));
        row.ContextMenu = menu;
        menu.IsOpen = true;
        if (fromKeyboard) ((MenuItem)menu.Items[0]).Focus();
    }
    private static MenuItem Menu(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    private void Items_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ItemsList);
        _dragItem = FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is null && FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is null
            ? FindItem(e.OriginalSource as DependencyObject) : null;
    }
    private void Items_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null || _dragItem.IsEditing) return;
        var position = e.GetPosition(ItemsList);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _dragItem;
        _dragItem = null;
        try { DragDrop.DoDragDrop(ItemsList, new DataObject(DragFormat, item), DragDropEffects.Move); }
        finally { ClearDropRow(); }
    }
    private void Items_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        if (e.Effects == DragDropEffects.None) return;
        ClearDropRow();
        _dropRow = FindRow(e.OriginalSource as DependencyObject);
        if (_dropRow is not null) { _dropRow.BorderBrush = Model.AccentBrush; _dropRow.BorderThickness = new(0, 0, 0, 2); }
        var y = e.GetPosition(ItemsScroller).Y;
        if (y < 24) ItemsScroller.ScrollToVerticalOffset(ItemsScroller.VerticalOffset - 12);
        else if (y > ItemsScroller.ActualHeight - 24) ItemsScroller.ScrollToVerticalOffset(ItemsScroller.VerticalOffset + 12);
    }
    private void Items_Drop(object sender, DragEventArgs e)
    {
        ClearDropRow();
        if (e.Data.GetData(DragFormat) is not ChecklistItemViewModel item) return;
        var row = FindRow(e.OriginalSource as DependencyObject);
        var target = row?.DataContext as ChecklistItemViewModel;
        var insertion = target is null ? Model.Items.Count : Model.Items.IndexOf(target) + (e.GetPosition(row!).Y > row!.ActualHeight / 2 ? 1 : 0);
        if (Model.Items.IndexOf(item) < insertion) insertion--;
        Model.MoveItem(item, Math.Clamp(insertion, 0, Model.Items.Count - 1));
        e.Handled = true;
    }
    private void ClearDropRow() { if (_dropRow is null) return; _dropRow.BorderThickness = new(0); _dropRow = null; }
    private static Border? FindRow(DependencyObject? source)
    {
        while (source is not null)
        {
            // 控件模板也含 Border；拖放与键盘菜单必须定位到完整清单行。
            if (source is Border { Name: "ItemRow", DataContext: ChecklistItemViewModel } border) return border;
            source = GetParent(source);
        }
        return null;
    }
    private static ChecklistItemViewModel? FindItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: ChecklistItemViewModel item }) return item;
            source = GetParent(source);
        }
        return null;
    }
    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null) { if (source is T match) return match; source = GetParent(source); }
        return null;
    }
    private static DependencyObject? GetParent(DependencyObject source) => source is Visual || source is System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
}

