using Microsoft.Win32;

namespace Pinto.Services;

/// <summary>只处理便签局部资源；明暗偏好始终由套件宿主传入。</summary>
public sealed class ThemeService : IDisposable
{
    private ThemePreference _preference;
    public static bool IsDark { get; private set; }
    public event Action? Changed;
    public ThemeService(ThemePreference preference)
    {
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Apply(preference);
    }
    public void Apply(ThemePreference preference)
    {
        _preference = preference;
        IsDark = preference == ThemePreference.Dark;
        Changed?.Invoke();
    }
    public void ApplyResources(FrameworkElement target) => target.Resources = CreateResources();
    public static ResourceDictionary CreateResources()
    {
        var name = SystemParameters.HighContrast ? "HighContrast" : IsDark ? "Dark" : "Light";
        var dictionary = new ResourceDictionary();
        foreach (var file in new[] { name, "Tokens", "Controls" })
            dictionary.MergedDictionaries.Add(new() { Source = new Uri($"pack://application:,,,/Pinto;component/Themes/{file}.xaml", UriKind.Absolute) });
        ApplyTextScale(dictionary, ReadTextScale());
        return dictionary;
    }
    public static SolidColorBrush GetAccentBrush(AccentColor color)
    {
        if (SystemParameters.HighContrast) return SystemColors.HighlightBrush;
        var hex = (color, IsDark) switch
        {
            (AccentColor.Purple, true) => "#C4A4EC", (AccentColor.Purple, false) => "#8056B3",
            (AccentColor.Green, true) => "#94CFAD", (AccentColor.Green, false) => "#287454",
            (AccentColor.Amber, true) => "#DEBE87", (AccentColor.Amber, false) => "#92611D",
            (AccentColor.Rose, true) => "#E5A2B9", (AccentColor.Rose, false) => "#B44E75",
            (_, true) => "#8EB5FA", _ => "#2467C9"
        };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
    public static string AccentName(AccentColor color) => color switch
    {
        AccentColor.Blue => "蓝色", AccentColor.Purple => "紫色", AccentColor.Green => "绿色", AccentColor.Amber => "琥珀", _ => "粉色"
    };
    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        => Application.Current.Dispatcher.BeginInvoke(() => Apply(_preference));
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Application.Current.Dispatcher.BeginInvoke(() => Apply(_preference));
    }
    internal static void ApplyTextScale(ResourceDictionary resources, double scale)
    {
        scale = double.IsFinite(scale) ? Math.Clamp(scale, 1, 2.25) : 1;
        foreach (var (key, size) in new[] { ("DisplayFontSize", 20d), ("TitleFontSize", 17d), ("BodyFontSize", 14d), ("UtilityFontSize", 13d), ("CaptionFontSize", 12d) })
            resources[key] = size * scale;
    }
    private static double ReadTextScale()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
            return key?.GetValue("TextScaleFactor") is int value ? value / 100d : 1;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException) { return 1; }
    }
    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }
}


