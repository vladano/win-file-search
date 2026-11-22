using System.Collections.Concurrent;
using FileSearch.Core;

namespace FileSearch.Core.Tests;

internal sealed class CollectingLogger : ISearchLogger
{
    public ConcurrentQueue<(string Message, Exception? Exception)> Warnings { get; } = new();

    public void Warning(string message, Exception? exception = null) => Warnings.Enqueue((message, exception));
}

/// <summary>Logging of recovered problems, and the limits that protect against huge documents.</summary>
public sealed class DiagnosticsAndLimitsTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static (ReadStatus Status, string Text) Read(string path, SearchCriteria? c = null, ISearchLogger? log = null)
    {
        var status = FileTextReader.TryRead(path, new FileInfo(path).Length, c ?? new SearchCriteria(),
            log ?? NullSearchLogger.Instance, CancellationToken.None, out var text);
        return (status, text);
    }

    [Fact]
    public void UnreadableFilesAreLogged()
    {
        var log = new CollectingLogger();
        var bad = _dir.Write("bad.docx", "this is not a zip file");

        Assert.Equal(ReadStatus.Error, Read(bad, log: log).Status);

        var warning = Assert.Single(log.Warnings);
        Assert.Contains("bad.docx", warning.Message, StringComparison.Ordinal);
        Assert.NotNull(warning.Exception);
    }

    [Fact]
    public async Task TheEnginePassesProblemsToTheLogger()
    {
        _dir.Write("bad.docx", "this is not a zip file");
        _dir.Write("ok.txt", "ALIAS");
        var log = new CollectingLogger();
        var criteria = new SearchCriteria { Folders = [_dir.Root], ContainingText = "ALIAS" };

        var results = new ConcurrentBag<FileResult>();
        var summary = await new SearchEngine(log).RunAsync(criteria, results.Add);

        Assert.Equal(1, summary.Found);
        Assert.Equal(1, summary.Skipped);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void TheEngineCanBeUsedThroughItsInterface()
    {
        ISearchEngine engine = new SearchEngine();
        Assert.NotNull(engine);
    }

    [Fact]
    public void DocumentThatExpandsPastTheLimitIsTooLarge()
    {
        // A zip bomb in miniature: a few KB on disk, megabytes of text once unpacked.
        var path = _dir.PathOf("bomb.docx");
        var xml = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>"
                  + new string('a', 3_000_000) + "</w:t></w:r></w:p></w:body></w:document>";
        SampleFiles.Zip(path, ("word/document.xml", xml));
        Assert.True(new FileInfo(path).Length < 100_000, "the test file must be small on disk");

        var log = new CollectingLogger();
        var (status, text) = Read(path, new SearchCriteria { MaxContentFileSizeBytes = 1_000_000 }, log);

        Assert.Equal(ReadStatus.TooLarge, status);
        Assert.Equal("", text);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void DocumentWithinTheLimitStillReads()
    {
        var path = _dir.PathOf("fine.docx");
        SampleFiles.Docx(path, "small ALIAS");
        var (status, text) = Read(path, new SearchCriteria { MaxContentFileSizeBytes = 1_000_000 });
        Assert.Equal(ReadStatus.Ok, status);
        Assert.Contains("ALIAS", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentFileLargerThanTheLimitIsTooLarge()
    {
        var path = _dir.PathOf("a.docx");
        SampleFiles.Docx(path, "some text");
        Assert.Equal(ReadStatus.TooLarge, Read(path, new SearchCriteria { MaxContentFileSizeBytes = 10 }).Status);
    }

    [Fact]
    public void PauseControlCanBeDisposed()
    {
        var pause = new PauseControl();
        pause.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pause.Pause());
    }
}
