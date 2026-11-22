using System.Diagnostics;
using System.IO.Enumeration;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace FileSearch.Core;

/// <summary>
/// Recursive file/content search. One producer enumerates files that pass the name, size and date
/// filters; several workers read and match content in parallel.
/// </summary>
public sealed class SearchEngine : ISearchEngine
{
    private readonly ISearchLogger _log;

    public SearchEngine(ISearchLogger? logger = null) => _log = logger ?? NullSearchLogger.Instance;

    private readonly record struct Candidate(string Path, long Length, DateTime Modified);

    private sealed class Counters
    {
        public long Found, FoundBytes, Checked, CheckedBytes, Skipped;
    }

    /// <inheritdoc />
    public async Task<SearchSummary> RunAsync(
        SearchCriteria criteria,
        Action<FileResult> onResult,
        IProgress<SearchProgress>? progress = null,
        PauseControl? pause = null,
        CancellationToken ct = default)
    {
        var nameMatcher = new NameMatcher(criteria.FileName, criteria.NameMode, criteria.NameMatchCase);
        var contentMatcher = string.IsNullOrEmpty(criteria.ContainingText)
            ? null
            : ContentMatcher.Create(criteria.ContainingText, criteria.ContentMode, criteria.ContentMatchCase);

        if (criteria.Folders.Count == 0) throw new ArgumentException("Choose a folder to look in.");
        foreach (var f in criteria.Folders)
            if (!Directory.Exists(f)) throw new DirectoryNotFoundException($"Folder not found: {f}");

        var counters = new Counters();
        var clock = Stopwatch.StartNew();
        var channel = Channel.CreateBounded<Candidate>(new BoundedChannelOptions(SearchTuning.ChannelCapacity)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        SearchProgress Snapshot(SearchState s) => new(
            Interlocked.Read(ref counters.Found), Interlocked.Read(ref counters.FoundBytes),
            Interlocked.Read(ref counters.Checked), Interlocked.Read(ref counters.CheckedBytes),
            Interlocked.Read(ref counters.Skipped), s, clock.Elapsed);

        using var stopTicker = new CancellationTokenSource();
        var ticker = progress is null
            ? Task.CompletedTask
            : Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(SearchTuning.ProgressInterval);
                try
                {
                    while (await timer.WaitForNextTickAsync(stopTicker.Token))
                        progress.Report(Snapshot(pause?.IsPaused == true ? SearchState.Paused : SearchState.Running));
                }
                catch (OperationCanceledException) { }
            }, CancellationToken.None);

        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var folder in criteria.Folders)
                    foreach (var c in Enumerate(folder, criteria, nameMatcher))
                    {
                        pause?.WaitIfPaused(ct);
                        ct.ThrowIfCancellationRequested();
                        await channel.Writer.WriteAsync(c, ct);
                    }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, CancellationToken.None);

        var workers = Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount))
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var cand in channel.Reader.ReadAllAsync(ct))
                {
                    pause?.WaitIfPaused(ct);
                    var result = Process(cand, contentMatcher, criteria, counters, _log, ct);
                    if (result is null) continue;
                    Interlocked.Increment(ref counters.Found);
                    Interlocked.Add(ref counters.FoundBytes, result.Length);
                    onResult(result);
                }
            }, CancellationToken.None))
            .ToArray();

        var state = SearchState.Completed;
        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
            state = SearchState.Cancelled;
        }
        finally
        {
            await producer;
            stopTicker.Cancel();
            await ticker;
        }

        var final = Snapshot(state);
        progress?.Report(final);
        return new SearchSummary(final.Found, final.FoundBytes, final.Checked, final.CheckedBytes,
            final.Skipped, state, clock.Elapsed);
    }

    private static IEnumerable<Candidate> Enumerate(string folder, SearchCriteria c, NameMatcher nameMatcher)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = c.Subfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = 0, // include hidden and system files
            ReturnSpecialDirectories = false,
        };

        long? minBytes = c.MinSizeKb is { } mn ? mn * 1024 : null;
        long? maxBytes = c.MaxSizeKb is { } mx ? mx * 1024 : null;
        var excludes = c.ExcludeFolders;

        return new FileSystemEnumerable<Candidate>(
            folder,
            (ref FileSystemEntry e) => new Candidate(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.LocalDateTime),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) =>
            {
                if (e.IsDirectory) return false;
                if (!nameMatcher.IsMatch(e.FileName)) return false;
                if (minBytes is { } lo && e.Length <= lo) return false;
                if (maxBytes is { } hi && e.Length >= hi) return false;
                if (!c.Modified.IsEmpty && !c.Modified.Contains(e.LastWriteTimeUtc.LocalDateTime)) return false;
                if (!c.Created.IsEmpty && !c.Created.Contains(e.CreationTimeUtc.LocalDateTime)) return false;
                if (!c.Accessed.IsEmpty && !c.Accessed.Contains(e.LastAccessTimeUtc.LocalDateTime)) return false;
                return true;
            },
            ShouldRecursePredicate = (ref FileSystemEntry e) =>
            {
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0) return false; // avoid link loops
                foreach (var p in excludes)
                    if (FileSystemName.MatchesSimpleExpression(p, e.FileName, ignoreCase: true)) return false;
                return true;
            },
        };
    }

    private static FileResult? Process(Candidate f, ContentMatcher? matcher, SearchCriteria c, Counters k, ISearchLogger log, CancellationToken ct)
    {
        Interlocked.Increment(ref k.Checked);
        Interlocked.Add(ref k.CheckedBytes, f.Length);

        var name = Path.GetFileName(f.Path);
        var dir = Path.GetDirectoryName(f.Path) ?? "";

        if (matcher is null)
            return new FileResult(f.Path, name, dir, f.Length, f.Modified, []);

        var status = FileTextReader.TryRead(f.Path, f.Length, c, log, ct, out var text);
        if (status != ReadStatus.Ok)
        {
            Interlocked.Increment(ref k.Skipped);
            return null;
        }

        try
        {
            if (!matcher.IsMatch(text)) return null;
            var hits = CollectHits(text, matcher, c.MaxHitsPerFile, ct);
            return new FileResult(f.Path, name, dir, f.Length, f.Modified, hits);
        }
        catch (RegexMatchTimeoutException ex)
        {
            log.Warning($"Content search timed out, file skipped: {f.Path}", ex);
            Interlocked.Increment(ref k.Skipped);
            return null;
        }
    }

    private static List<Hit> CollectHits(string text, ContentMatcher matcher, int max, CancellationToken ct)
    {
        var hits = new List<Hit>();
        var ranges = new List<MatchRange>();
        var lineNo = 0;
        var pos = 0;
        while (pos <= text.Length && hits.Count < max)
        {
            var end = text.IndexOf('\n', pos);
            if (end < 0) end = text.Length;
            lineNo++;

            var line = text.Substring(pos, end - pos);
            if (line.Length > 0 && matcher.LineHasMatch(line))
            {
                matcher.FindInLine(line, ranges);
                hits.Add(MakeHit(lineNo, line, ranges));
            }

            if ((lineNo & (SearchTuning.CancelCheckLines - 1)) == 0) ct.ThrowIfCancellationRequested();
            pos = end + 1;
        }
        return hits;
    }

    private static Hit MakeHit(int lineNo, string line, List<MatchRange> ranges)
    {
        if (line.Length <= SearchTuning.MaxLineChars)
            return new Hit(lineNo, line, ranges.ToArray());

        // Long line: show a window around the first match.
        var first = ranges.Count > 0 ? ranges[0].Start : 0;
        var start = Math.Max(0, first - SearchTuning.LongLineLeadChars);
        var len = Math.Min(SearchTuning.MaxLineChars, line.Length - start);
        var shifted = new List<MatchRange>();
        foreach (var r in ranges)
        {
            var s = r.Start - start;
            if (s < 0 || s >= len) continue;
            shifted.Add(new MatchRange(s, Math.Min(r.Length, len - s)));
        }
        return new Hit(lineNo, line.Substring(start, len), shifted);
    }
}
