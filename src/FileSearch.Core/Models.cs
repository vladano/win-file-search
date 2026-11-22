namespace FileSearch.Core;

public sealed record Hit(int LineNumber, string Text, IReadOnlyList<MatchRange> Matches);

public sealed record FileResult(
    string FullPath,
    string Name,
    string Directory,
    long Length,
    DateTime Modified,
    IReadOnlyList<Hit> Hits);

public enum SearchState { Running, Paused, Completed, Cancelled }

public sealed record SearchProgress(
    long Found,
    long FoundBytes,
    long Checked,
    long CheckedBytes,
    long Skipped,
    SearchState State,
    TimeSpan Elapsed);

public sealed record SearchSummary(
    long Found,
    long FoundBytes,
    long Checked,
    long CheckedBytes,
    long Skipped,
    SearchState State,
    TimeSpan Elapsed);

/// <summary>Lets a running search be paused and resumed.</summary>
public sealed class PauseControl : IDisposable
{
    private readonly ManualResetEventSlim _gate = new(true);

    public bool IsPaused => !_gate.IsSet;
    public void Pause() => _gate.Reset();
    public void Resume() => _gate.Set();
    public void WaitIfPaused(CancellationToken ct) => _gate.Wait(ct);

    /// <summary>Releases the gate. Call only after the search that uses it has finished.</summary>
    public void Dispose() => _gate.Dispose();
}
