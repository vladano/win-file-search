namespace FileSearch.Core;

/// <summary>
/// Runs a search. Lets callers (view models, tests) swap in a fake instead of walking the real file system.
/// </summary>
public interface ISearchEngine
{
    /// <summary>
    /// Runs a search. <paramref name="onResult"/> is invoked from worker threads, so callers must
    /// marshal to their own thread. Throws ArgumentException/FormatException/DirectoryNotFoundException
    /// for invalid criteria before any work starts.
    /// </summary>
    Task<SearchSummary> RunAsync(
        SearchCriteria criteria,
        Action<FileResult> onResult,
        IProgress<SearchProgress>? progress = null,
        PauseControl? pause = null,
        CancellationToken ct = default);
}

/// <summary>Receives problems the engine recovers from (unreadable files, timeouts) so they can be logged.</summary>
public interface ISearchLogger
{
    void Warning(string message, Exception? exception = null);
}

/// <summary>Logger that discards everything. Used when the caller does not supply one.</summary>
public sealed class NullSearchLogger : ISearchLogger
{
    public static NullSearchLogger Instance { get; } = new();

    private NullSearchLogger() { }

    public void Warning(string message, Exception? exception = null) { }
}
