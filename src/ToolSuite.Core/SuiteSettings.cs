using System.Text.Json;

namespace ToolSuite.Core;

public enum ThemePreference { System, Light, Dark }

public sealed class SuiteSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool StartWithWindows { get; set; }
    public Dictionary<string, bool> EnabledTools { get; set; } = ToolIds.All.ToDictionary(id => id, _ => true);
    public Dictionary<string, bool> ExpandedSettingsSections { get; set; } = [];
}

public sealed class SuiteSettingsStore
{
    private readonly string root;
    public SuiteSettingsStore(string root) => this.root = Path.GetFullPath(root);
    public string RootDirectory => root;
    public string ModuleDirectory(string id)
    {
        if (id.Length == 0 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("无效的模块标识。", nameof(id));
        return Path.Combine(root, "modules", id);
    }
    public SuiteSettings Load()
    {
        var path = Path.Combine(root, "suite.json");
        if (!File.Exists(path)) return new();
        // 损坏配置必须显式报错，不能静默覆盖用户选择。
        var settings = JsonSerializer.Deserialize<SuiteSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("套件设置为空。");
        if (!Enum.IsDefined(settings.Theme)) throw new InvalidDataException("无效的主题配置。");
        settings.ExpandedSettingsSections ??= [];
        foreach (var id in ToolIds.All) settings.EnabledTools.TryAdd(id, true);
        return settings;
    }
    public void Save(SuiteSettings settings)
    {
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "suite.json");
        var temporary = destination + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
