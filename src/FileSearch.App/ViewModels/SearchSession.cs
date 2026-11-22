using System.Collections.Concurrent;
using System.Windows.Threading;
using FileSearch.Core;

namespace FileSearch.App.ViewModels;

/// <summary>
/// Runs one search and hands the results to the UI in batches. Owns the cancellation token, the pause
/// gate and the flush timer, so <see cref="SearchTabViewModel"/> only deals with form state and display.
/// Must be created and used on the UI thread.
/// </summary>
internal sealed class SearchSession : IDisposable
{
    private readonly ISearchEngine _engine;
    private readonly Action<IReadOnlyList<FileResult>> _onBatch;
    private readonly Action<SearchProgress> _onProgress;
    private readonly ConcurrentQueue<FileResult> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private CancellationTokenSource? _cts;
    private PauseControl? _pause;

    /// <param name="onBatch">Receives found files on the UI thread.</param>
    /// <param name="onProgress">Receives progress on the UI thread.</param>
    public SearchSession(ISearchEngine engine, Action<IReadOnlyList<FileResult>> onBatch, Action<SearchProgress> onProgress)
    {
        _engine = engine;
        _onBatch = onBatch;
        _onProgress = onProgress;
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = UiTuning.FlushInterval };
        _flushTimer.Tick += (_, _) => Flush(UiTuning.FlushBatchSize);
    }

    /// <summary>
    /// Runs the search. Every result found has been passed to <c>onBatch</c> by the time this returns or throws.
    /// </summary>
    public async Task<SearchSummary> RunAsync(SearchCriteria criteria)
    {
        while (_pending.TryDequeue(out _)) { }

        _cts = new CancellationTokenSource();
        _pause = new PauseControl();
        _flushTimer.Start();
        try
        {
            return await _engine.RunAsync(criteria, _pending.Enqueue, new Progress<SearchProgress>(_onProgress), _pause, _cts.Token);
        }
        finally
        {
            _flushTimer.Stop();
            Flush(int.MaxValue);
            _cts.Dispose();
            _cts = null;
            _pause.Dispose();
            _pause = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _pause?.Resume(); // a paused search must wake up to notice the cancellation
    }

    /// <summary>Pauses a running search or resumes a paused one. Returns the new paused state.</summary>
    public bool TogglePause()
    {
        if (_pause is null) return false;
        if (_pause.IsPaused) _pause.Resume();
        else _pause.Pause();
        return _pause.IsPaused;
    }

    public void Dispose()
    {
        _flushTimer.Stop();
        Stop();
    }

    private void Flush(int max)
    {
        var batch = new List<FileResult>();
        while (batch.Count < max && _pending.TryDequeue(out var r)) batch.Add(r);
        if (batch.Count > 0) _onBatch(batch);
    }
}
