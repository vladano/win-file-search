using System.Collections.Specialized;
using System.ComponentModel;
using FileSearch.App.Services;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

public sealed class SearchTabViewModelTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private SearchTabViewModel NewTab()
    {
        var tab = Vm.NewMain().SelectedTab!;
        tab.LookIn = _dir.Root;
        return tab;
    }

    private static Task Run(SearchTabViewModel t) => t.StartCommand.ExecuteAsync(null);

    // ---- initial state ----

    [Fact]
    public void Defaults()
    {
        var t = Vm.NewMain().SelectedTab!;
        Assert.Equal(SearchTabViewModel.NewSearchTitle, t.Title);
        Assert.Equal("*", t.FileName);
        Assert.True(t.Subfolders);
        Assert.True(t.SearchDocuments);
        Assert.Equal("Ready", t.StatusText);
        Assert.True(t.IsPristine);
        Assert.False(t.IsRunning);
        Assert.True(t.StartCommand.CanExecute(null));
        Assert.False(t.StopCommand.CanExecute(null));
        Assert.False(t.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void TabIsNoLongerPristineOnceUsed()
    {
        var t = Vm.NewMain().SelectedTab!;
        t.ContainingText = "x";
        Assert.False(t.IsPristine);
    }

    // ---- running a search ----

    [Fact]
    public async Task FindsFilesAndFillsInStatistics()
    {
        _dir.Write("a.prg", "one\nALIAS two\n");
        _dir.Write("sub/b.prg", "ALIAS");
        _dir.Write("c.txt", "ALIAS");
        var t = NewTab();
        t.FileName = "*.prg";
        t.ContainingText = "ALIAS";

        await Run(t);

        Assert.Equal(["a.prg", "b.prg"], t.Results.Select(r => r.Name).Order().ToArray());
        Assert.StartsWith("2 items", t.FoundText);
        Assert.StartsWith("2 items", t.CheckedText);
        Assert.StartsWith("Finished in", t.StatusText);
        Assert.False(t.IsError);
        Assert.False(t.IsRunning);
        Assert.True(t.HasSearched);
        Assert.Equal("ALIAS", t.Title);
        Assert.Contains("a.prg", t.ReportText);
        Assert.Contains("ALIAS two", t.ReportText);
        Assert.Equal(2, t.AllResults.Count());
    }

    [Fact]
    public async Task NameOnlySearchUsesTheFileNameAsTitle()
    {
        _dir.Write("a.cs", "x");
        var t = NewTab();
        t.FileName = "*.cs";

        await Run(t);

        Assert.Equal("*.cs", t.Title);
        Assert.Single(t.Results);
    }

    [Fact]
    public async Task SubfoldersCheckboxIsHonoured()
    {
        _dir.Write("a.txt", "x");
        _dir.Write("sub/b.txt", "x");
        var t = NewTab();
        t.Subfolders = false;

        await Run(t);

        Assert.Equal(["a.txt"], t.Results.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task RunningAgainReplacesThePreviousResults()
    {
        _dir.Write("a.txt", "ALIAS");
        var t = NewTab();
        t.ContainingText = "ALIAS";
        await Run(t);
        Assert.Single(t.Results);

        t.ContainingText = "nothing like this";
        await Run(t);

        Assert.Empty(t.Results);
        Assert.Null(t.SelectedResult);
        Assert.Equal("", t.ReportText);
        Assert.StartsWith("0 items", t.FoundText);
    }

    [Fact]
    public async Task ModesMapToSearchModes()
    {
        _dir.Write("a.txt", "alpha\nbeta\n");
        _dir.Write("b.txt", "alpha only\n");
        _dir.Write("log12.txt", "x");
        var t = NewTab();

        t.ContentMode = ContentMode.Boolean;
        t.ContainingText = "alpha AND beta";
        await Run(t);
        Assert.Equal(["a.txt"], t.Results.Select(r => r.Name).ToArray());

        t.ContentMode = ContentMode.Regex;
        t.ContainingText = @"^alpha\s+only$";
        await Run(t);
        Assert.Equal(["b.txt"], t.Results.Select(r => r.Name).ToArray());

        t.ContainingText = "";
        t.NameMode = NameMode.Regex;
        t.FileName = @"^log\d+\.txt$";
        await Run(t);
        Assert.Equal(["log12.txt"], t.Results.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task MatchCaseToggles()
    {
        _dir.Write("Readme.TXT", "alias");
        var t = NewTab();
        t.FileName = "*.txt";
        t.ContainingText = "ALIAS";
        await Run(t);
        Assert.Single(t.Results);

        t.ContentMatchCase = true;
        await Run(t);
        Assert.Empty(t.Results);

        t.ContentMatchCase = false;
        t.NameMatchCase = true;
        await Run(t);
        Assert.Empty(t.Results);
    }

    [Fact]
    public async Task SizeBoxesAreInKilobytes()
    {
        _dir.Write("small.txt", "x");
        _dir.Write("large.txt", new string('x', 5000));
        var t = NewTab();

        t.MinSize = "4";
        await Run(t);
        Assert.Equal(["large.txt"], t.Results.Select(r => r.Name).ToArray());

        t.MinSize = "";
        t.MaxSize = "4";
        await Run(t);
        Assert.Equal(["small.txt"], t.Results.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task DateFiltersCoverWholeDays()
    {
        var old = _dir.Write("old.txt", "x");
        File.SetLastWriteTime(old, DateTime.Today.AddDays(-30).AddHours(12));
        _dir.Write("today.txt", "x");
        var t = NewTab();

        t.ModifiedAfter.Value = DateTime.Today;           // from the start of today
        await Run(t);
        Assert.Equal(["today.txt"], t.Results.Select(r => r.Name).ToArray());

        t.ModifiedAfter.Clear();
        t.ModifiedBefore.Value = DateTime.Today;          // up to the end of today
        await Run(t);
        Assert.Equal(["old.txt", "today.txt"], t.Results.Select(r => r.Name).Order().ToArray());

        t.ModifiedBefore.Value = DateTime.Today.AddDays(-30); // the old file's own day counts
        await Run(t);
        Assert.Equal(["old.txt"], t.Results.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task ExcludedFoldersAreSplitOnSemicolonsAndCommas()
    {
        _dir.Write("keep/a.txt", "x");
        _dir.Write(".git/b.txt", "x");
        _dir.Write("node_modules/c.txt", "x");
        var t = NewTab();
        t.ExcludeFolders = ".git; node_modules";

        await Run(t);

        Assert.Equal(["a.txt"], t.Results.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task SeveralFoldersSeparatedBySemicolon()
    {
        using var other = new TempDir();
        _dir.Write("a.txt", "x");
        other.Write("b.txt", "x");
        var t = NewTab();
        t.LookIn = _dir.Root + ";" + other.Root;

        await Run(t);

        Assert.Equal(["a.txt", "b.txt"], t.Results.Select(r => r.Name).Order().ToArray());
    }

    [Fact]
    public async Task DocumentSearchCanBeSwitchedOff()
    {
        var dir = _dir.Root;
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(dir, "a.docx"), System.IO.Compression.ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>ALIAS</w:t></w:r></w:p></w:document>");
        var t = NewTab();
        t.ContainingText = "ALIAS";

        await Run(t);
        Assert.Single(t.Results);

        t.SearchDocuments = false;
        await Run(t);
        Assert.Empty(t.Results);
    }

    [Fact]
    public async Task SearchesAreRememberedInTheHistoryLists()
    {
        var main = Vm.NewMain();
        var t = main.SelectedTab!;
        t.LookIn = _dir.Root;
        t.FileName = "*.cs";
        t.ContainingText = "needle";

        await Run(t);

        Assert.Equal("*.cs", main.Settings.NameHistory[0]);
        Assert.Equal("needle", main.Settings.TextHistory[0]);
        Assert.Equal(_dir.Root, main.Settings.FolderHistory[0]);
        // the typed values must survive the history update
        Assert.Equal(("*.cs", "needle", _dir.Root), (t.FileName, t.ContainingText, t.LookIn));
    }

    // ---- errors ----

    [Fact]
    public async Task MissingFolderIsReportedNotThrown()
    {
        var t = NewTab();
        t.LookIn = "";
        await Run(t);
        Assert.True(t.IsError);
        Assert.Contains("Look in", t.StatusText);
        Assert.False(t.IsRunning);
        Assert.False(t.HasSearched);
    }

    [Fact]
    public async Task NonExistentFolderIsReported()
    {
        var t = NewTab();
        t.LookIn = Path.Combine(_dir.Root, "does-not-exist");
        await Run(t);
        Assert.True(t.IsError);
        Assert.StartsWith("Error:", t.StatusText);
        Assert.False(t.IsRunning);
        Assert.True(t.StartCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public async Task InvalidSizeIsReported(string size)
    {
        var t = NewTab();
        t.MinSize = size;
        await Run(t);
        Assert.True(t.IsError);
        Assert.Contains("whole number", t.StatusText);
    }

    [Fact]
    public async Task InvalidRegexIsReported()
    {
        var t = NewTab();
        t.ContentMode = ContentMode.Regex;
        t.ContainingText = "(";
        await Run(t);
        Assert.True(t.IsError);
        Assert.False(t.IsRunning);
    }

    [Fact]
    public async Task InvalidBooleanExpressionIsReported()
    {
        var t = NewTab();
        t.ContentMode = ContentMode.Boolean;
        t.ContainingText = "a AND (b";
        await Run(t);
        Assert.True(t.IsError);
        Assert.Contains("')'", t.StatusText);
    }

    [Fact]
    public async Task ErrorIsClearedByTheNextGoodSearch()
    {
        var t = NewTab();
        t.MinSize = "abc";
        await Run(t);
        Assert.True(t.IsError);

        t.MinSize = "";
        await Run(t);
        Assert.False(t.IsError);
        Assert.StartsWith("Finished", t.StatusText);
    }

    // ---- stop / pause ----

    [Fact]
    public async Task StopCancelsARunningSearch()
    {
        for (var i = 0; i < 300; i++) _dir.Write($"d{i % 20}/f{i}.txt", new string('x', 20_000) + " needle");
        var t = NewTab();
        t.ContainingText = "needle";

        var run = Run(t);
        await MainViewModelTests.WaitUntil(() => t.IsRunning);
        t.StopCommand.Execute(null);
        await run.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.False(t.IsRunning);
        Assert.True(t.StatusText.StartsWith("Cancelled", StringComparison.Ordinal) || t.StatusText.StartsWith("Finished", StringComparison.Ordinal), t.StatusText);
    }

    [Fact]
    public async Task PauseTogglesAndStopWhilePausedStillEnds()
    {
        for (var i = 0; i < 300; i++) _dir.Write($"d{i % 20}/f{i}.txt", new string('x', 20_000) + " needle");
        var t = NewTab();
        t.ContainingText = "needle";

        var run = Run(t);
        await MainViewModelTests.WaitUntil(() => t.IsRunning);
        if (t.IsRunning)
        {
            t.PauseCommand.Execute(null);
            if (t.IsRunning) Assert.True(t.IsPaused);
            t.StopCommand.Execute(null);
        }
        await run.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.False(t.IsRunning);
        Assert.False(t.IsPaused);
    }

    // ---- definitions and misc commands ----

    [Fact]
    public void DefinitionRoundTrip()
    {
        var a = Vm.NewMain().SelectedTab!;
        a.FileName = "*.prg";
        a.ContainingText = "ALIAS";
        a.LookIn = @"C:\a;C:\b";
        a.Subfolders = false;
        a.NameMatchCase = true;
        a.ContentMatchCase = true;
        a.NameMode = NameMode.Regex;
        a.ContentMode = ContentMode.Regex;
        a.MinSize = "1";
        a.MaxSize = "9";
        a.SearchDocuments = false;
        a.ExcludeFolders = ".git";
        a.ModifiedAfter.Value = new DateTime(2024, 1, 1);
        a.CreatedBefore.Value = new DateTime(2024, 2, 2);
        a.AccessedAfter.Value = new DateTime(2024, 3, 3);

        var b = Vm.NewMain().SelectedTab!;
        b.ApplyDefinition(a.ToDefinition());

        Assert.Equivalent(a.ToDefinition(), b.ToDefinition());
        Assert.True(b.ModifiedAfter.Use);
        Assert.False(b.ModifiedBefore.Use);
    }

    [Fact]
    public void ApplyDefinitionFallsBackOnUnknownModes()
    {
        var t = Vm.NewMain().SelectedTab!;
        t.ApplyDefinition(new SearchDefinition { NameMode = (NameMode)99, ContentMode = (ContentMode)(-4) });
        Assert.Equal(NameMode.Dos, t.NameMode);
        Assert.Equal(ContentMode.Plain, t.ContentMode);
    }

    [Fact]
    public void ClearDatesClearsAllSix()
    {
        var t = Vm.NewMain().SelectedTab!;
        var d = DateTime.Today;
        t.ModifiedAfter.Value = t.ModifiedBefore.Value = t.CreatedAfter.Value = d;
        t.CreatedBefore.Value = t.AccessedAfter.Value = t.AccessedBefore.Value = d;

        t.ClearDatesCommand.Execute(null);

        foreach (var f in new[] { t.ModifiedAfter, t.ModifiedBefore, t.CreatedAfter, t.CreatedBefore, t.AccessedAfter, t.AccessedBefore })
        {
            Assert.Null(f.Value);
            Assert.False(f.Use);
        }
    }

    [Fact]
    public void ModeListsMatchTheUi()
    {
        var t = Vm.NewMain().SelectedTab!;
        Assert.Equal(["DOS Expression", "Regular expression"], t.NameModes.Select(m => m.Label));
        Assert.Equal(["Plain text", "Boolean", "Regular expression"], t.ContentModes.Select(m => m.Label));
        Assert.Equal(["Simple", "Expert"], t.Modes.Select(m => m.Label));
    }

    [Fact]
    public void EveryModeHasAnOptionInTheUi()
    {
        // Adding a value to an enum without adding it to the combo box would make it unreachable.
        var t = Vm.NewMain().SelectedTab!;
        Assert.Equal(Enum.GetValues<NameMode>().Order(), t.NameModes.Select(m => m.Value).Order());
        Assert.Equal(Enum.GetValues<ContentMode>().Order(), t.ContentModes.Select(m => m.Value).Order());
        Assert.Equal(Enum.GetValues<UiMode>().Order(), t.Modes.Select(m => m.Value).Order());
    }

    [Fact]
    public void DisposeIsSafeToCallTwice()
    {
        var t = Vm.NewMain().SelectedTab!;
        t.Dispose();
        t.Dispose();
    }
}
