using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using FileSearch.Core;

namespace FileSearch.Core.Tests;

public sealed class SearchEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fs-test-" + Guid.NewGuid().ToString("N"));

    public SearchEngineTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub", "deep"));
        Directory.CreateDirectory(Path.Combine(_root, "node_modules"));
        File.WriteAllText(Path.Combine(_root, "a.prg"), "USE x\nSELECT 1\nALIAS foo\n");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "nothing here\n");
        File.WriteAllText(Path.Combine(_root, "sub", "c.prg"), "line1\r\nalias bar and ALIAS baz\r\n");
        File.WriteAllText(Path.Combine(_root, "sub", "deep", "d.prg"), "deep ALIAS\n");
        File.WriteAllText(Path.Combine(_root, "node_modules", "e.prg"), "ALIAS hidden\n");
        File.WriteAllBytes(Path.Combine(_root, "bin.dat"), [1, 2, 0, 0, (byte)'A', (byte)'L', (byte)'I', (byte)'A', (byte)'S']);
        File.WriteAllText(Path.Combine(_root, "utf16.txt"), "ALIAS in utf16", Encoding.Unicode);
        MakeDocx(Path.Combine(_root, "doc.docx"), "Quarterly ALIAS report");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void MakeDocx(string path, string text)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var e = zip.CreateEntry("word/document.xml");
        using var w = new StreamWriter(e.Open());
        w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>"
                + text + "</w:t></w:r></w:p></w:body></w:document>");
    }

    private static async Task<(List<FileResult> results, SearchSummary summary)> Run(SearchCriteria c, CancellationToken ct = default)
    {
        var bag = new ConcurrentBag<FileResult>();
        var summary = await new SearchEngine().RunAsync(c, bag.Add, ct: ct);
        return (bag.OrderBy(r => r.FullPath).ToList(), summary);
    }

    private SearchCriteria Criteria(string name = "*.prg", string text = "ALIAS", bool sub = true) => new()
    {
        Folders = [_root],
        FileName = name,
        ContainingText = text,
        Subfolders = sub,
    };

    [Fact]
    public async Task FindsContentInSubfolders()
    {
        var (results, summary) = await Run(Criteria());
        Assert.Equal(4, results.Count);
        Assert.Equal(4, summary.Found);
        Assert.Equal(SearchState.Completed, summary.State);
    }

    [Fact]
    public async Task NoSubfolders()
    {
        var (results, _) = await Run(Criteria(sub: false));
        Assert.Single(results);
        Assert.Equal("a.prg", results[0].Name);
    }

    [Fact]
    public async Task ReportsLineNumbersAndRanges()
    {
        var (results, _) = await Run(Criteria());
        var c = results.Single(r => r.Name == "c.prg");
        var hit = Assert.Single(c.Hits);
        Assert.Equal(2, hit.LineNumber);
        Assert.Equal(2, hit.Matches.Count);
        Assert.Equal(new MatchRange(0, 5), hit.Matches[0]);
    }

    [Fact]
    public async Task NameOnlySearchWhenNoText()
    {
        var (results, summary) = await Run(Criteria(text: ""));
        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.Empty(r.Hits));
        Assert.Equal(4, summary.Checked);
    }

    [Fact]
    public async Task ExcludedFoldersAreSkipped()
    {
        var c = new SearchCriteria
        {
            Folders = [_root], FileName = "*.prg", ContainingText = "ALIAS", ExcludeFolders = ["node_modules"],
        };
        var (results, _) = await Run(c);
        Assert.DoesNotContain(results, r => r.Name == "e.prg");
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task BinaryFilesAreSkippedAndUtf16Found()
    {
        var (results, summary) = await Run(Criteria(name: "*.dat;*.txt"));
        Assert.Equal(["utf16.txt"], results.Select(r => r.Name).ToArray());
        Assert.True(summary.Skipped >= 1);
    }

    [Fact]
    public async Task SearchesDocx()
    {
        var (results, _) = await Run(Criteria(name: "*.docx"));
        Assert.Single(results);

        var off = Criteria(name: "*.docx");
        var (none, _) = await Run(new SearchCriteria
        {
            Folders = off.Folders, FileName = off.FileName, ContainingText = off.ContainingText, SearchDocuments = false,
        });
        Assert.Empty(none);
    }

    [Fact]
    public async Task SizeFilter()
    {
        File.WriteAllText(Path.Combine(_root, "big.prg"), new string('x', 5000) + " ALIAS");
        var (results, _) = await Run(new SearchCriteria
        {
            Folders = [_root], FileName = "*.prg", ContainingText = "ALIAS", MinSizeKb = 4,
        });
        Assert.Equal(["big.prg"], results.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task DateFilter()
    {
        var old = Path.Combine(_root, "a.prg");
        File.SetLastWriteTime(old, new DateTime(2020, 1, 1));
        var (results, _) = await Run(new SearchCriteria
        {
            Folders = [_root], FileName = "*.prg", ContainingText = "ALIAS",
            Modified = new DateRange { Before = new DateTime(2021, 1, 1) },
        });
        Assert.Equal(["a.prg"], results.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task CancelStopsSearch()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (_, summary) = await Run(Criteria(), cts.Token);
        Assert.Equal(SearchState.Cancelled, summary.State);
    }

    [Fact]
    public async Task MissingFolderThrows() =>
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            Run(new SearchCriteria { Folders = [Path.Combine(_root, "nope")] }));

    [Fact]
    public async Task InvalidRegexThrows() =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            Run(new SearchCriteria { Folders = [_root], ContainingText = "(", ContentMode = ContentMode.Regex }));
}
