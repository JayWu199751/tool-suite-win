using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinto.Core;

/// <summary>负责完整快照的校验、原子替换与导入事务；界面仅在成功后应用新快照。</summary>
public sealed class DataStore
{
    public const long MaximumFileBytes = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public string DirectoryPath { get; }
    public string StatePath => Path.Combine(DirectoryPath, "state.json");
    public string PreviousPath => StatePath + ".previous";

    public DataStore(string directoryPath) => DirectoryPath = Path.GetFullPath(directoryPath);

    public LoadResult Load()
    {
        if (!File.Exists(StatePath) && !File.Exists(PreviousPath))
            return new(new AppData(), true);

        try
        {
            return new(ReadFile(StatePath), false);
        }
        catch (Exception error) when (IsDataError(error))
        {
            if (File.Exists(PreviousPath))
            {
                try
                {
                    var previous = ReadFile(PreviousPath);
                    var preserved = PreserveDamagedFile();
                    // 损坏的主文件已移走，下一次保存不会覆盖可恢复的上一份快照。
                    return new(previous, false, $"已从上一份自动保存恢复便签。损坏文件保留在：\n{preserved ?? StatePath}");
                }
                catch (Exception recoveryError) when (IsDataError(recoveryError))
                {
                    throw new InvalidDataException("便签数据和上一份自动保存均无法读取。原文件未被覆盖，请从备份恢复。", recoveryError);
                }
            }

            throw new InvalidDataException("便签数据无法读取。原文件未被覆盖，请保留数据文件并从备份恢复。", error);
        }
    }

    public void Save(AppData data)
    {
        Validate(data);
        Directory.CreateDirectory(DirectoryPath);
        WriteAtomically(StatePath, Serialize(data), PreviousPath);
    }

    public void Export(AppData data, string destination)
    {
        Validate(data);
        if (Path.GetFullPath(destination).Equals(StatePath, StringComparison.OrdinalIgnoreCase)
            || Path.GetFullPath(destination).Equals(PreviousPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择其他备份位置，不能覆盖正在使用的数据文件。");
        WriteAtomically(Path.GetFullPath(destination), Serialize(data));
    }

    public AppData ReadBackup(string source) => ReadFile(source);

    public AppData RecoverFromBackup(string source)
    {
        var incoming = ReadFile(source);
        Directory.CreateDirectory(DirectoryPath);
        var stamp = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        foreach (var path in new[] { StatePath, PreviousPath })
            if (File.Exists(path)) File.Copy(path, path + ".corrupt-" + stamp);
        WriteAtomically(StatePath, Serialize(incoming));
        return incoming;
    }

    public ImportResult Import(AppData current, string source, ImportMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var incoming = ReadFile(source); // 必须先完整校验，失败时不修改当前数据。
        var next = Clone(current);
        string? safetyBackup = null;

        if (mode == ImportMode.Append)
        {
            foreach (var note in incoming.Notes)
            {
                note.Id = Guid.NewGuid();
                foreach (var item in note.Items) item.Id = Guid.NewGuid();
                if (note.Left.HasValue) note.Left += 24;
                if (note.Top.HasValue) note.Top += 24;
                next.Notes.Add(note);
            }
        }
        else
        {
            next.Notes = incoming.Notes;
        }

        Validate(next);
        if (mode == ImportMode.Replace)
        {
            var backupDirectory = Path.Combine(DirectoryPath, "Backups");
            Directory.CreateDirectory(backupDirectory);
            safetyBackup = Path.Combine(backupDirectory, $"替换前-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
            WriteAtomically(safetyBackup, Serialize(current));
        }

        Save(next);
        return new(next, incoming.Notes.Count, safetyBackup);
    }

    public static AppData Clone(AppData data)
    {
        Validate(data);
        return JsonSerializer.Deserialize<AppData>(Serialize(data), JsonOptions)!;
    }

    private static byte[] Serialize(AppData data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        if (bytes.LongLength > MaximumFileBytes)
            throw new InvalidDataException("便签数据超过 20 MB，请先拆分备份或减少内容。");
        return bytes;
    }

    private static AppData ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("备份文件超过 20 MB，无法导入。");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format) || format.GetString() != "Pinto"
            || !root.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var version) || version != 1
            || !root.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("这不是受支持的 Pinto 备份文件（版本 1）。");

        var data = root.Deserialize<AppData>(JsonOptions) ?? throw new InvalidDataException("备份内容为空。");
        Validate(data);
        return data;
    }

    public static void Validate(AppData data)
    {
        if (data.Format != "Pinto" || data.SchemaVersion != 1 || !Enum.IsDefined(data.Theme)
            || data.Notes is null || data.Notes.Count > 500)
            throw new InvalidDataException("便签数据格式无效，或便签数量超过 500 张。");
        var noteIds = new HashSet<Guid>();
        var itemIds = new HashSet<Guid>();
        foreach (var note in data.Notes)
        {
            if (note is null || note.Id == Guid.Empty || !noteIds.Add(note.Id)
                || note.Title is null || note.Title.Length > 200 || !Enum.IsDefined(note.Accent)
                || !EdgeDocking.IsValid(note.DockEdges)
                || note.Items is null || note.Items.Count > 10000
                || !double.IsFinite(note.Width) || !double.IsFinite(note.Height)
                || (note.Left.HasValue && !double.IsFinite(note.Left.Value))
                || (note.Top.HasValue && !double.IsFinite(note.Top.Value)))
                throw new InvalidDataException("便签信息无效，存在重复标识、过长标题或无效窗口位置。");
            foreach (var item in note.Items)
                if (item is null || item.Id == Guid.Empty || !itemIds.Add(item.Id)
                    || string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 50000)
                    throw new InvalidDataException("清单项无效，存在空白内容、重复标识或超过 50000 字的内容。");
        }
    }

    private string? PreserveDamagedFile()
    {
        if (!File.Exists(StatePath)) return null;
        var target = Path.Combine(DirectoryPath, $"state.corrupt-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        File.Move(StatePath, target);
        return target;
    }

    private static void WriteAtomically(string path, byte[] content, string? previousPath = null)
    {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path)) File.Replace(temporary, path, previousPath);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsDataError(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException;
}
