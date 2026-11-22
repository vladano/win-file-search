using System.Text.Json;
using System.Text.Json.Serialization;
using FileSearch.Core;

namespace FileSearch.App.Services;

/// <summary>A saved search (criteria only, no results).</summary>
public sealed class SearchDefinition
{
    /// <summary>Version 1 stored the modes as combo box positions. Version 2 stores them by name.</summary>
    public const int CurrentVersion = 2;

    public const string FileFilter = "File Search definition (*.fsearch)|*.fsearch";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Version { get; set; } = CurrentVersion;

    public string FileName { get; set; } = "*";
    public string ContainingText { get; set; } = "";
    public string LookIn { get; set; } = "";
    public bool Subfolders { get; set; } = true;
    public bool NameMatchCase { get; set; }
    public bool ContentMatchCase { get; set; }
    public NameMode NameMode { get; set; } = NameMode.Dos;
    public ContentMode ContentMode { get; set; } = ContentMode.Plain;
    public string MinSize { get; set; } = "";
    public string MaxSize { get; set; } = "";
    public bool SearchDocuments { get; set; } = true;
    public string ExcludeFolders { get; set; } = "";
    public DateTime? ModifiedAfter { get; set; }
    public DateTime? ModifiedBefore { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public DateTime? AccessedAfter { get; set; }
    public DateTime? AccessedBefore { get; set; }

    // Only read from version 1 files. Never written: Load() converts them to the enums above.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NameModeIndex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContentModeIndex { get; set; }

    public void Save(string path)
    {
        Version = CurrentVersion;
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static SearchDefinition Load(string path)
    {
        var def = JsonSerializer.Deserialize<SearchDefinition>(File.ReadAllText(path), JsonOptions) ?? new SearchDefinition();
        def.MigrateFromLegacy();
        return def;
    }

    private void MigrateFromLegacy()
    {
        if (NameModeIndex is { } n)
            NameMode = n >= 1 ? NameMode.Regex : NameMode.Dos;
        if (ContentModeIndex is { } c)
            ContentMode = c switch { <= 0 => ContentMode.Plain, 1 => ContentMode.Boolean, _ => ContentMode.Regex };
        NameModeIndex = null;
        ContentModeIndex = null;
        Version = CurrentVersion;
    }
}
