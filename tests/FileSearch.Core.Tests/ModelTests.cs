using FileSearch.Core;

namespace FileSearch.Core.Tests;

public class SearchCriteriaTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SplitListOfNothingIsEmpty(string? input) => Assert.Empty(SearchCriteria.SplitList(input));

    [Fact]
    public void SplitListTrimsAndDropsEmptyEntries() =>
        Assert.Equal(["C:\\a", "D:\\b"], SearchCriteria.SplitList(" C:\\a ;; D:\\b ; "));

    [Fact]
    public void CommaIsOnlyASeparatorWhenAllowed()
    {
        Assert.Equal(["a,b"], SearchCriteria.SplitList("a,b"));
        Assert.Equal(["a", "b"], SearchCriteria.SplitList("a,b", allowComma: true));
    }

    [Fact]
    public void Defaults()
    {
        var c = new SearchCriteria();
        Assert.Equal("*", c.FileName);
        Assert.True(c.Subfolders);
        Assert.True(c.SearchDocuments);
        Assert.True(c.Modified.IsEmpty);
        Assert.Equal(100L * 1024 * 1024, c.MaxContentFileSizeBytes);
    }
}

public class DateRangeTests
{
    private static readonly DateTime Jan1 = new(2024, 1, 1);
    private static readonly DateTime Feb1 = new(2024, 2, 1);

    [Fact]
    public void EmptyRangeAcceptsEverythingAndReportsEmpty()
    {
        var r = new DateRange();
        Assert.True(r.IsEmpty);
        Assert.True(r.Contains(DateTime.MinValue));
        Assert.True(r.Contains(DateTime.MaxValue));
    }

    [Fact]
    public void AfterIsInclusive()
    {
        var r = new DateRange { After = Jan1 };
        Assert.False(r.IsEmpty);
        Assert.True(r.Contains(Jan1));
        Assert.False(r.Contains(Jan1.AddTicks(-1)));
    }

    [Fact]
    public void BeforeIsInclusive()
    {
        var r = new DateRange { Before = Feb1 };
        Assert.True(r.Contains(Feb1));
        Assert.False(r.Contains(Feb1.AddTicks(1)));
    }

    [Fact]
    public void BothBounds()
    {
        var r = new DateRange { After = Jan1, Before = Feb1 };
        Assert.True(r.Contains(new DateTime(2024, 1, 15)));
        Assert.False(r.Contains(new DateTime(2023, 12, 31)));
        Assert.False(r.Contains(new DateTime(2024, 2, 2)));
    }
}

public class PauseControlTests
{
    [Fact]
    public void StartsRunning()
    {
        var p = new PauseControl();
        Assert.False(p.IsPaused);
        p.WaitIfPaused(CancellationToken.None); // must not block
    }

    [Fact]
    public async Task PauseBlocksUntilResumed()
    {
        var p = new PauseControl();
        p.Pause();
        Assert.True(p.IsPaused);

        var waiter = Task.Run(() => p.WaitIfPaused(CancellationToken.None));
        await Task.Delay(200);
        Assert.False(waiter.IsCompleted);

        p.Resume();
        await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(p.IsPaused);
    }

    [Fact]
    public async Task CancellationReleasesAPausedWaiter()
    {
        var p = new PauseControl();
        p.Pause();
        using var cts = new CancellationTokenSource();
        var waiter = Task.Run(() => p.WaitIfPaused(cts.Token));
        await Task.Delay(100);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}

public class ReportBuilderTests
{
    private static FileResult Result(string name, params Hit[] hits) =>
        new($@"C:\data\{name}", name, @"C:\data", 2048, new DateTime(2024, 5, 6, 7, 8, 9), hits);

    private static Hit H(int line, string text) => new(line, text, []);

    [Fact]
    public void TextReportListsFilesAndHits()
    {
        var text = ReportBuilder.ToText([Result("a.prg", H(3, "   ALIAS x")), Result("b.prg")]);
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith(@"C:\data\a.prg", lines[0]);
        Assert.Contains("kB", lines[0]);
        Assert.Contains("2024-05-06 07:08", lines[0]);
        Assert.Contains("3: ALIAS x", lines[1]); // leading whitespace trimmed
        Assert.Contains("b.prg", lines[2]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void TextReportOfNothingIsEmpty() => Assert.Equal("", ReportBuilder.ToText([]));

    [Fact]
    public void CsvHasHeaderAndOneRowPerHit()
    {
        var csv = ReportBuilder.ToCsv([Result("a.prg", H(1, "one"), H(2, "two"))]);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Path,Name,Location,SizeKB,Modified,Line,Text", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith(",1,one", lines[1]);
        Assert.EndsWith(",2,two", lines[2]);
        Assert.Contains(",2,", lines[1]); // SizeKB, invariant culture
    }

    [Fact]
    public void CsvRowForFileWithoutHitsHasEmptyLineAndText()
    {
        var csv = ReportBuilder.ToCsv([Result("a.prg")]);
        var row = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.EndsWith(",,", row);
    }

    [Fact]
    public void CsvEscapesCommasQuotesAndNewlines()
    {
        var csv = ReportBuilder.ToCsv([Result("a,b.prg", H(1, "say \"hi\", ok"))]);
        Assert.Contains("\"C:\\data\\a,b.prg\"", csv);
        Assert.Contains("\"say \"\"hi\"\", ok\"", csv);
    }
}
