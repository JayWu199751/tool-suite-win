namespace Pinto.Views;

public partial class ImportDialog : Window
{
    public ImportMode? SelectedMode { get; private set; }
    public ImportDialog(int incomingCount, int existingCount)
    {
        Resources = Services.ThemeService.CreateResources();
        InitializeComponent();
        Summary.Text = $"备份包含 {incomingCount} 张便签，当前有 {existingCount} 张。\n重复追加同一份备份会生成新的副本。";
        AppendButton.IsEnabled = existingCount + incomingCount <= 500;
        SourceInitialized += (_, _) => Services.NativeWindow.ApplyDialogAppearance(this);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == BackgroundProperty && IsLoaded) Services.NativeWindow.ApplyDialogAppearance(this);
    }
    private void Append_Click(object sender, RoutedEventArgs e) { SelectedMode = ImportMode.Append; DialogResult = true; }
    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "将替换全部现有便签。替换前会自动创建备份，是否继续？", "替换全部便签", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        SelectedMode = ImportMode.Replace;
        DialogResult = true;
    }
}

