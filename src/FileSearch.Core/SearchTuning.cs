namespace FileSearch.Core;

/// <summary>Engine constants in one place, so tuning the search does not mean hunting through files.</summary>
internal static class SearchTuning
{
    /// <summary>Files queued between the enumerating producer and the reading workers.</summary>
    public const int ChannelCapacity = 2048;

    /// <summary>How often progress is reported to the caller.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Longest hit line kept in full. Longer lines are cut to a window around the first match.</summary>
    public const int MaxLineChars = 400;

    /// <summary>Characters kept before the first match when a long line is cut.</summary>
    public const int LongLineLeadChars = 100;

    /// <summary>Lines scanned between cancellation checks (must be a power of two).</summary>
    public const int CancelCheckLines = 0x4000;

    /// <summary>Longest a content regex may run on one file before the file is skipped.</summary>
    public static readonly TimeSpan ContentRegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Longest a file name regex may run on one name before it counts as no match.</summary>
    public static readonly TimeSpan NameRegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Default for <see cref="SearchCriteria.MaxContentFileSizeBytes"/>.</summary>
    public const long DefaultMaxContentFileSizeBytes = 100L * 1024 * 1024;

    /// <summary>Default for <see cref="SearchCriteria.MaxHitsPerFile"/>.</summary>
    public const int DefaultMaxHitsPerFile = 1000;
}
