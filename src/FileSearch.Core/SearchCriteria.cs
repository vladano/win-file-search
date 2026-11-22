namespace FileSearch.Core;

public enum NameMode { Dos, Regex }

public enum ContentMode { Plain, Regex, Boolean }

/// <summary>Inclusive local-time date range. Null bounds are ignored.</summary>
public sealed class DateRange
{
    public DateTime? After { get; init; }
    public DateTime? Before { get; init; }

    public bool IsEmpty => After is null && Before is null;

    public bool Contains(DateTime localTime) =>
        (After is null || localTime >= After) && (Before is null || localTime <= Before);
}

public sealed class SearchCriteria
{
    /// <summary>Folders to search ("Look in").</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>File name pattern(s), e.g. "*.cs;*.xaml" (DOS) or a regular expression.</summary>
    public string FileName { get; init; } = "*";
    public NameMode NameMode { get; init; } = NameMode.Dos;
    public bool NameMatchCase { get; init; }

    /// <summary>Text to find inside files. Empty means "match on name only".</summary>
    public string ContainingText { get; init; } = "";
    public ContentMode ContentMode { get; init; } = ContentMode.Plain;
    public bool ContentMatchCase { get; init; }

    public bool Subfolders { get; init; } = true;

    /// <summary>Only files strictly larger than this many kB.</summary>
    public long? MinSizeKb { get; init; }
    /// <summary>Only files strictly smaller than this many kB.</summary>
    public long? MaxSizeKb { get; init; }

    public DateRange Modified { get; init; } = new();
    public DateRange Created { get; init; } = new();
    public DateRange Accessed { get; init; } = new();

    /// <summary>Search inside Office (docx/xlsx/pptx) and PDF documents.</summary>
    public bool SearchDocuments { get; init; } = true;

    /// <summary>Folder name patterns to skip, e.g. "node_modules", ".git".</summary>
    public IReadOnlyList<string> ExcludeFolders { get; init; } = [];

    public long MaxContentFileSizeBytes { get; init; } = SearchTuning.DefaultMaxContentFileSizeBytes;
    public int MaxHitsPerFile { get; init; } = SearchTuning.DefaultMaxHitsPerFile;

    /// <summary>Splits a user-entered list on ';' (and ',' when <paramref name="allowComma"/>).</summary>
    public static IReadOnlyList<string> SplitList(string? text, bool allowComma = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        char[] seps = allowComma ? [';', ','] : [';'];
        return text.Split(seps, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
