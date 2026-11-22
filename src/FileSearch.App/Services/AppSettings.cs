using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileSearch.App.ViewModels;

namespace FileSearch.App.Services;

/// <summary>User settings persisted to %AppData%\FileSearch\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Version 1 stored the mode as 0/1 (ModeIndex). Version 2 stores it by name.</summary>
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Version { get; set; } = CurrentVersion;

    public ObservableCollection<string> NameHistory { get; set; } = [];
    public ObservableCollection<string> TextHistory { get; set; } = [];
    public ObservableCollection<string> FolderHistory { get; set; } = [];

    public UiMode Mode { get; set; } = UiMode.Expert;

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Maximized { get; set; }

    // Only read from version 1 files (0 = Simple, 1 = Expert). Never written.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ModeIndex { get; set; }

    /// <summary>
    /// Where <see cref="Save(string?)"/> writes by default. Set by <see cref="Load"/>. Settings created
    /// with <c>new AppSettings()</c> (for example in tests) have no file and never touch the disk.
    /// </summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    /// <summary>Called when saving fails, so the application can log it. Saving stays best-effort.</summary>
    [JsonIgnore]
    public Action<Exception>? SaveFailed { get; set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSearch", "settings.json");

    /// <summary>Loads settings from <paramref name="path"/> (default: %AppData%). A missing or corrupt file gives defaults.</summary>
    public static AppSettings Load(string? path = null, Action<Exception>? onError = null)
    {
        path ??= DefaultPath;
        var settings = new AppSettings();
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable settings: start fresh.
            onError?.Invoke(ex);
            settings = new AppSettings();
        }

        settings.MigrateFromLegacy();
        settings.FilePath = path;
        return settings;
    }

    /// <summary>Saves to <paramref name="path"/>, or to <see cref="FilePath"/> when omitted (no-op if neither is set).</summary>
    public void Save(string? path = null)
    {
        path ??= FilePath;
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            // Settings are best-effort.
            SaveFailed?.Invoke(ex);
        }
    }

    /// <summary>Moves or inserts <paramref name="value"/> at the top of a history list.</summary>
    public static void Remember(ObservableCollection<string> list, string? value, int max = UiTuning.HistoryLimit)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var idx = list.IndexOf(value);
        if (idx == 0) return;
        if (idx > 0) list.Move(idx, 0);
        else list.Insert(0, value);
        while (list.Count > max) list.RemoveAt(list.Count - 1);
    }

    private void MigrateFromLegacy()
    {
        if (ModeIndex is { } m) Mode = m == 0 ? UiMode.Simple : UiMode.Expert;
        ModeIndex = null;
        Version = CurrentVersion;
    }
}
