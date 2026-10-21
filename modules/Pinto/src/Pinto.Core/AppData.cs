namespace Pinto.Core;

public enum ThemePreference { System, Light, Dark }
public enum AccentColor { Blue, Purple, Green, Amber, Rose }
public enum ImportMode { Append, Replace }

[Flags]
public enum DockEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

public sealed class AppData
{
    public string Format { get; set; } = "Pinto";
    public int SchemaVersion { get; set; } = 1;
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public List<NoteData> Notes { get; set; } = [];
}

public sealed class NoteData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "新便签";
    public AccentColor Accent { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 324;
    public double Height { get; set; } = 273;
    public bool IsVisible { get; set; } = true;
    public DockEdges DockEdges { get; set; }
    public List<ChecklistItemData> Items { get; set; } = [];
}

public sealed class ChecklistItemData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public bool IsCompleted { get; set; }
}

public sealed record LoadResult(AppData Data, bool IsFirstRun, string? Notice = null);
public sealed record ImportResult(AppData Data, int ImportedCount, string? SafetyBackupPath);
