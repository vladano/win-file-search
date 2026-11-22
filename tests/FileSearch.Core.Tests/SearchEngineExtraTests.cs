using System.Diagnostics;
using FileSearch.Core;

namespace FileSearch.Core.Tests;

/// <summary>Engine behaviour not covered by <see cref="SearchEngineTests"/>.</summary>
public sealed class SearchEngineExtraTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private SearchCriteria C(string name = "*", string text = "", Action<SearchCriteriaBuilder>? tweak = null)
    {
        var b = new SearchCriteriaBuilder { Folders = [_dir.Root], FileName = name, ContainingText = text };
        tweak?.Invoke(b);
        return b.Build();
    }

    // ---- name matching ----

    [Fact]
    public async Task NameMatchCaseControlsWhichFilesMatch()
    {
        _dir.Write("Readme.TXT", "x");
        _dir.Write("notes.txt", "x");

        var (insensitive, _) = await Engine.Run(C("*.txt"));
        var (sensitive, _) = await Engine.Run(C("*.txt", tweak: b => b.NameMatchCase = true));

        Assert.Equal(["notes.txt", "Readme.TXT"], Engine.Names(insensitive));
        Assert.Equal(["notes.txt"], Engine.Names(sensitive));
    }

    [Fact]
    public async Task RegexFileNames()
    {
        _dir.Write("log1.txt", "x");
        _dir.Write("log22.txt", "x");
        _dir.Write("logx.txt", "x");

        var (r, _) = await Engine.Run(C(@"^log\d+\.txt$", tweak: b => b.NameMode = NameMode.Regex));
        Assert.Equal(["log1.txt", "log22.txt"], Engine.Names(r));
    }

    [Fact]
    public async Task SeveralPatternsAtOnce()
    {
        _dir.Write("a.cs", "x");
        _dir.Write("b.xaml", "x");
        _dir.Write("c.txt", "x");

        var (r, _) = await Engine.Run(C("*.cs;*.xaml"));
        Assert.Equal(["a.cs", "b.xaml"], Engine.Names(r));
    }

    [Fact]
    public async Task HiddenFilesAreSearched()
    {
        var p = _dir.Write("secret.txt", "ALIAS");
        File.SetAttributes(p, FileAttributes.Hidden);

        var (r, _) = await Engine.Run(C(text: "ALIAS"));
        Assert.Equal(["secret.txt"], Engine.Names(r));
    }

    // ---- content matching ----

    [Fact]
    public async Task ContentMatchCase()
    {
        _dir.Write("a.txt", "alias");

        var (any, _) = await Engine.Run(C(text: "ALIAS"));
        var (exact, _) = await Engine.Run(C(text: "ALIAS", tweak: b => b.ContentMatchCase = true));

        Assert.Single(any);
        Assert.Empty(exact);
    }

    [Fact]
    public async Task RegexContent()
    {
        _dir.Write("a.txt", "id=123\nid=abc\n");
        _dir.Write("b.txt", "nothing\n");

        var (r, _) = await Engine.Run(C(text: @"id=\d+", tweak: b => b.ContentMode = ContentMode.Regex));
        var hit = Assert.Single(Assert.Single(r).Hits);
        Assert.Equal(1, hit.LineNumber);
        Assert.Equal(new MatchRange(0, 6), Assert.Single(hit.Matches));
    }

    [Fact]
    public async Task BooleanContentAcrossLines()
    {
        _dir.Write("both.txt", "alpha\nbeta\n");
        _dir.Write("one.txt", "alpha only\n");

        var (r, _) = await Engine.Run(C(text: "alpha AND beta", tweak: b => b.ContentMode = ContentMode.Boolean));
        Assert.Equal(["both.txt"], Engine.Names(r));
        Assert.Equal(2, r[0].Hits.Count); // both words are highlighted on their own lines
    }

    [Fact]
    public async Task NegatedBooleanFindsFilesWithoutTheWord()
    {
        _dir.Write("clean.txt", "public\n");
        _dir.Write("dirty.txt", "secret\n");

        var (r, _) = await Engine.Run(C(text: "NOT secret", tweak: b => b.ContentMode = ContentMode.Boolean));
        Assert.Equal(["clean.txt"], Engine.Names(r));
        Assert.Empty(r[0].Hits);
    }

    [Fact]
    public async Task WindowsLineEndingsGiveCorrectLineNumbers()
    {
        _dir.Write("a.txt", "one\r\ntwo\r\nthree ALIAS\r\n");
        var (r, _) = await Engine.Run(C(text: "ALIAS"));
        Assert.Equal(3, r[0].Hits[0].LineNumber);
    }

    [Fact]
    public async Task HitsAreCappedPerFile()
    {
        _dir.Write("a.txt", string.Concat(Enumerable.Repeat("ALIAS\n", 50)));
        var (r, _) = await Engine.Run(C(text: "ALIAS", tweak: b => b.MaxHitsPerFile = 5));
        Assert.Equal(5, r[0].Hits.Count);
    }

    [Fact]
    public async Task VeryLongLinesAreWindowedAroundTheMatch()
    {
        _dir.Write("long.txt", new string('x', 1000) + "ALIAS" + new string('y', 1000));
        var (r, _) = await Engine.Run(C(text: "ALIAS"));

        var hit = Assert.Single(r[0].Hits);
        Assert.True(hit.Text.Length <= 400);
        var m = Assert.Single(hit.Matches);
        Assert.Equal("ALIAS", hit.Text.Substring(m.Start, m.Length));
    }

    [Fact]
    public async Task FilesOverTheContentLimitAreSkippedButCounted()
    {
        _dir.Write("big.txt", "ALIAS" + new string('x', 5000));
        _dir.Write("small.txt", "ALIAS");

        var (r, s) = await Engine.Run(C(text: "ALIAS", tweak: b => b.MaxContentFileSizeBytes = 1000));
        Assert.Equal(["small.txt"], Engine.Names(r));
        Assert.Equal(1, s.Skipped);
        Assert.Equal(2, s.Checked);
    }

    [Fact]
    public async Task DocumentFormatsAreSearched()
    {
        SampleFiles.Docx(_dir.PathOf("a.docx"), "alpha ALIAS");
        SampleFiles.Xlsx(_dir.PathOf("b.xlsx"), "beta ALIAS");
        SampleFiles.Pptx(_dir.PathOf("c.pptx"), "gamma ALIAS");
        SampleFiles.Pdf(_dir.PathOf("d.pdf"), "delta ALIAS");
        _dir.Write("e.txt", "no match");

        var (r, _) = await Engine.Run(C(text: "ALIAS"));
        Assert.Equal(["a.docx", "b.xlsx", "c.pptx", "d.pdf"], Engine.Names(r).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task UnreadableDocumentDoesNotStopTheSearch()
    {
        _dir.Write("bad.docx", "not a zip");
        _dir.Write("good.txt", "ALIAS");

        var (r, s) = await Engine.Run(C(text: "ALIAS"));
        Assert.Equal(["good.txt"], Engine.Names(r));
        Assert.Equal(SearchState.Completed, s.State);
        Assert.Equal(1, s.Skipped);
    }

    // ---- filters ----

    [Fact]
    public async Task MaxSizeFilter()
    {
        _dir.Write("tiny.txt", "ALIAS");
        _dir.Write("big.txt", "ALIAS" + new string('x', 5000));

        var (r, _) = await Engine.Run(C(text: "ALIAS", tweak: b => b.MaxSizeKb = 4));
        Assert.Equal(["tiny.txt"], Engine.Names(r));
    }

    [Fact]
    public async Task SizeFilterIsStrict()
    {
        _dir.Write("exactly1k.txt", new string('x', 1024));
        var (above, _) = await Engine.Run(C(tweak: b => b.MinSizeKb = 1));
        var (below, _) = await Engine.Run(C(tweak: b => b.MaxSizeKb = 1));
        Assert.Empty(above);
        Assert.Empty(below);
    }

    [Fact]
    public async Task CreatedAndAccessedDateFilters()
    {
        var old = _dir.Write("old.txt", "x");
        _dir.Write("new.txt", "x");
        File.SetCreationTime(old, new DateTime(2019, 6, 1));
        File.SetLastAccessTime(old, new DateTime(2019, 6, 2));

        var (created, _) = await Engine.Run(C(tweak: b => b.Created = new DateRange { Before = new DateTime(2020, 1, 1) }));
        var (accessed, _) = await Engine.Run(C(tweak: b => b.Accessed = new DateRange { Before = new DateTime(2020, 1, 1) }));
        var (modifiedAfter, _) = await Engine.Run(C(tweak: b => b.Modified = new DateRange { After = DateTime.Now.AddDays(-1) }));

        Assert.Equal(["old.txt"], Engine.Names(created));
        Assert.Equal(["old.txt"], Engine.Names(accessed));
        Assert.Contains("new.txt", Engine.Names(modifiedAfter));
    }

    [Fact]
    public async Task ExcludedFoldersAreMatchedByWildcard()
    {
        _dir.Write("src/a.txt", "x");
        _dir.Write("obj/Debug/b.txt", "x");
        _dir.Write("obj2/c.txt", "x");

        var (r, _) = await Engine.Run(C(tweak: b => b.ExcludeFolders = ["obj*"]));
        Assert.Equal(["a.txt"], Engine.Names(r));
    }

    // ---- folders ----

    [Fact]
    public async Task SeveralLookInFolders()
    {
        using var other = new TempDir();
        _dir.Write("a.txt", "ALIAS");
        other.Write("b.txt", "ALIAS");

        var (r, _) = await Engine.Run(C(text: "ALIAS", tweak: b => b.Folders = [_dir.Root, other.Root]));
        Assert.Equal(["a.txt", "b.txt"], Engine.Names(r));
    }

    [Fact]
    public async Task NoFoldersThrows() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Engine.Run(new SearchCriteria()));

    [Fact]
    public async Task EmptyFolderCompletesWithNothing()
    {
        var (r, s) = await Engine.Run(C());
        Assert.Empty(r);
        Assert.Equal(SearchState.Completed, s.State);
        Assert.Equal(0, s.Checked);
    }

    // ---- progress, summary, pause, cancel ----

    private sealed class SyncProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];
        public void Report(T value) { lock (Items) Items.Add(value); }
    }

    [Fact]
    public async Task FinalProgressMatchesTheSummary()
    {
        _dir.Write("a.txt", "ALIAS");
        _dir.Write("b.txt", "other");
        var progress = new SyncProgress<SearchProgress>();

        var summary = await new SearchEngine().RunAsync(C(text: "ALIAS"), _ => { }, progress);

        Assert.Equal(1, summary.Found);
        Assert.Equal(2, summary.Checked);
        Assert.Equal(SearchState.Completed, summary.State);
        Assert.True(summary.CheckedBytes >= summary.FoundBytes);
        var last = progress.Items[^1];
        Assert.Equal((summary.Found, summary.Checked, summary.State), (last.Found, last.Checked, last.State));
    }

    [Fact]
    public async Task ResultCallbackReceivesEveryFoundFile()
    {
        for (var i = 0; i < 200; i++) _dir.Write($"d{i % 10}/f{i}.txt", "ALIAS");
        var count = 0;
        var summary = await new SearchEngine().RunAsync(C(text: "ALIAS"), _ => Interlocked.Increment(ref count));
        Assert.Equal(200, count);
        Assert.Equal(200, summary.Found);
    }

    [Fact]
    public async Task PausedSearchWaitsUntilResumed()
    {
        _dir.Write("a.txt", "ALIAS");
        var pause = new PauseControl();
        pause.Pause();

        var run = Engine.Run(C(text: "ALIAS"), pause: pause);
        await Task.Delay(400);
        Assert.False(run.IsCompleted);

        pause.Resume();
        var (r, s) = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(r);
        Assert.Equal(SearchState.Completed, s.State);
    }

    [Fact]
    public async Task CancellingWhilePausedEndsTheSearch()
    {
        _dir.Write("a.txt", "ALIAS");
        var pause = new PauseControl();
        pause.Pause();
        using var cts = new CancellationTokenSource();

        var run = Engine.Run(C(text: "ALIAS"), cts.Token, pause);
        await Task.Delay(200);
        cts.Cancel();

        var (_, s) = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(SearchState.Cancelled, s.State);
    }

    [Fact]
    public async Task InvalidBooleanExpressionThrowsBeforeSearching() =>
        await Assert.ThrowsAsync<FormatException>(() =>
            Engine.Run(C(text: "a AND (b", tweak: b => b.ContentMode = ContentMode.Boolean)));

    // ---- junctions ----

    [Fact]
    public async Task JunctionLoopsAreNotFollowed()
    {
        _dir.Write("a.txt", "ALIAS");
        var junction = _dir.PathOf("sub/loop");
        Directory.CreateDirectory(_dir.PathOf("sub"));
        var mk = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{junction}\" \"{_dir.Root}\"",
        })!;
        var output = await mk.StandardOutput.ReadToEndAsync() + await mk.StandardError.ReadToEndAsync();
        await mk.WaitForExitAsync();
        Assert.True(mk.ExitCode == 0 && Directory.Exists(junction), "could not create a directory junction: " + output);

        try
        {
            var (r, s) = await Engine.Run(C(text: "ALIAS")).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(["a.txt"], Engine.Names(r));
            Assert.Equal(SearchState.Completed, s.State);
        }
        finally
        {
            Directory.Delete(junction); // removes only the link, not the target
        }
    }
}

/// <summary>SearchCriteria has init-only properties; this lets tests tweak them conveniently.</summary>
public sealed class SearchCriteriaBuilder
{
    public IReadOnlyList<string> Folders { get; set; } = [];
    public string FileName { get; set; } = "*";
    public NameMode NameMode { get; set; }
    public bool NameMatchCase { get; set; }
    public string ContainingText { get; set; } = "";
    public ContentMode ContentMode { get; set; }
    public bool ContentMatchCase { get; set; }
    public bool Subfolders { get; set; } = true;
    public long? MinSizeKb { get; set; }
    public long? MaxSizeKb { get; set; }
    public DateRange Modified { get; set; } = new();
    public DateRange Created { get; set; } = new();
    public DateRange Accessed { get; set; } = new();
    public bool SearchDocuments { get; set; } = true;
    public IReadOnlyList<string> ExcludeFolders { get; set; } = [];
    public long MaxContentFileSizeBytes { get; set; } = 100L * 1024 * 1024;
    public int MaxHitsPerFile { get; set; } = 1000;

    public SearchCriteria Build() => new()
    {
        Folders = Folders, FileName = FileName, NameMode = NameMode, NameMatchCase = NameMatchCase,
        ContainingText = ContainingText, ContentMode = ContentMode, ContentMatchCase = ContentMatchCase,
        Subfolders = Subfolders, MinSizeKb = MinSizeKb, MaxSizeKb = MaxSizeKb,
        Modified = Modified, Created = Created, Accessed = Accessed, SearchDocuments = SearchDocuments,
        ExcludeFolders = ExcludeFolders, MaxContentFileSizeBytes = MaxContentFileSizeBytes, MaxHitsPerFile = MaxHitsPerFile,
    };
}
