using System.Reflection;
using FileSearch.App.Services;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

/// <summary>Old files must keep loading after a format change. These tests pin that down.</summary>
public class FormatMigrationTests
{
    [Fact]
    public void VersionOneDefinitionIndicesBecomeEnums()
    {
        using var dir = new TempDir();
        var path = dir.Write("old.fsearch", """{ "FileName": "*.cs", "NameModeIndex": 1, "ContentModeIndex": 2 }""");

        var d = SearchDefinition.Load(path);

        Assert.Equal(NameMode.Regex, d.NameMode);
        Assert.Equal(ContentMode.Regex, d.ContentMode);
        Assert.Equal(SearchDefinition.CurrentVersion, d.Version);
        Assert.Null(d.NameModeIndex);
        Assert.Null(d.ContentModeIndex);
    }

    [Theory]
    [InlineData(-4, ContentMode.Plain)]
    [InlineData(0, ContentMode.Plain)]
    [InlineData(1, ContentMode.Boolean)]
    [InlineData(2, ContentMode.Regex)]
    [InlineData(99, ContentMode.Regex)]
    public void VersionOneContentIndexMapping(int index, ContentMode expected)
    {
        using var dir = new TempDir();
        var path = dir.Write("old.fsearch", $$"""{ "ContentModeIndex": {{index}} }""");
        Assert.Equal(expected, SearchDefinition.Load(path).ContentMode);
    }

    [Fact]
    public void DefinitionsAreWrittenWithModeNamesAndVersion()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Root, "new.fsearch");

        new SearchDefinition { ContentMode = ContentMode.Boolean, NameMode = NameMode.Regex }.Save(path);

        var json = File.ReadAllText(path);
        Assert.Contains("\"ContentMode\": \"Boolean\"", json, StringComparison.Ordinal);
        Assert.Contains("\"NameMode\": \"Regex\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Version\": 2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ModeIndex", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, UiMode.Simple)]
    [InlineData(1, UiMode.Expert)]
    public void VersionOneSettingsModeIndexBecomesMode(int index, UiMode expected)
    {
        using var dir = new TempDir();
        var path = dir.Write("settings.json", $$"""{ "ModeIndex": {{index}}, "Left": 5 }""");

        var s = AppSettings.Load(path);

        Assert.Equal(expected, s.Mode);
        Assert.Equal(5, s.Left);
        Assert.Equal(AppSettings.CurrentVersion, s.Version);
    }

    [Fact]
    public void SettingsAreWrittenWithModeNameAndVersion()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Root, "settings.json");

        new AppSettings { Mode = UiMode.Simple }.Save(path);

        var json = File.ReadAllText(path);
        Assert.Contains("\"Mode\": \"Simple\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Version\": 2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ModeIndex", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWithoutAFileDoNotWriteAnywhere()
    {
        var s = new AppSettings();
        Assert.Null(s.FilePath);
        s.Save(); // must be a harmless no-op, not a write to the real %AppData%
    }

    [Fact]
    public void LoadedSettingsSaveBackToTheSameFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Root, "settings.json");

        var s = AppSettings.Load(path);
        s.Left = 42;
        s.Save();

        Assert.Equal(42, AppSettings.Load(path).Left);
    }

    [Fact]
    public void SaveFailureIsReportedNotThrown()
    {
        using var dir = new TempDir();
        var blocker = dir.Write("file.txt", "x");
        Exception? reported = null;
        var s = new AppSettings { SaveFailed = ex => reported = ex };

        s.Save(Path.Combine(blocker, "settings.json"));

        Assert.NotNull(reported);
    }

    [Fact]
    public void CorruptSettingsAreReported()
    {
        using var dir = new TempDir();
        var path = dir.Write("settings.json", "{ nope");
        Exception? reported = null;

        var s = AppSettings.Load(path, ex => reported = ex);

        Assert.NotNull(reported);
        Assert.Equal(UiMode.Expert, s.Mode);
    }
}

/// <summary>
/// A saved search is copied between SearchDefinition and the view model by hand. If someone adds a field to
/// one side and forgets the mapping, this test fails and names the field.
/// </summary>
public class DefinitionMappingTests
{
    private static readonly HashSet<string> NotCriteria = ["Version", "NameModeIndex", "ContentModeIndex"];

    private static IEnumerable<PropertyInfo> CriteriaProperties() =>
        typeof(SearchDefinition).GetProperties().Where(p => p.CanWrite && !NotCriteria.Contains(p.Name));

    private static object NonDefault(PropertyInfo p, int seed)
    {
        var current = p.GetValue(new SearchDefinition());
        var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (type == typeof(string)) return "value-" + p.Name;
        if (type == typeof(bool)) return !(bool)current!;
        if (type == typeof(DateTime)) return new DateTime(2024, 1, 1).AddDays(seed);
        if (type.IsEnum) return Enum.GetValues(type).Cast<object>().First(v => !v.Equals(current));
        throw new NotSupportedException($"Teach this test about {p.Name} ({type.Name}).");
    }

    [Fact]
    public void EveryDefinitionFieldSurvivesTheViewModel()
    {
        var def = new SearchDefinition();
        var seed = 0;
        foreach (var p in CriteriaProperties()) p.SetValue(def, NonDefault(p, seed++));

        var tab = Vm.NewMain().SelectedTab!;
        tab.ApplyDefinition(def);
        var back = tab.ToDefinition();

        foreach (var p in CriteriaProperties())
            Assert.True(Equals(p.GetValue(def), p.GetValue(back)), $"{p.Name} was lost between SearchDefinition and the view model.");
    }

    [Fact]
    public void EveryDefinitionFieldSurvivesTheFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Root, "all.fsearch");
        var def = new SearchDefinition();
        var seed = 0;
        foreach (var p in CriteriaProperties()) p.SetValue(def, NonDefault(p, seed++));

        def.Save(path);
        var loaded = SearchDefinition.Load(path);

        foreach (var p in CriteriaProperties())
            Assert.True(Equals(p.GetValue(def), p.GetValue(loaded)), $"{p.Name} was lost when saving and loading.");
    }
}

public sealed class DialogFlowTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static FileResult Result(string name = "a.txt") =>
        new($@"C:\data\{name}", name, @"C:\data", 2048, new DateTime(2024, 1, 2, 3, 4, 5), [new Hit(1, "ALIAS, here", [])]);

    private AppLog NewLog(out string path)
    {
        path = Path.Combine(_dir.Root, "logs", "test.log");
        return new AppLog(path);
    }

    // ---- injected engine ----

    [Fact]
    public async Task TheTabSearchesThroughTheInjectedEngine()
    {
        var engine = new FakeEngine(Result("a.txt"), Result("b.txt"));
        var tab = Vm.NewMain(engine).SelectedTab!;
        tab.LookIn = @"C:\data";

        await tab.StartCommand.ExecuteAsync(null);

        Assert.Equal(["a.txt", "b.txt"], tab.Results.Select(r => r.Name).ToArray());
        Assert.Equal([@"C:\data"], engine.LastCriteria!.Folders);
        Assert.Contains("Finished", tab.StatusText, StringComparison.Ordinal);
        Assert.Contains("2 items", tab.FoundText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedEngineFailuresAreShownAndLogged()
    {
        var log = NewLog(out var logPath);
        var engine = new FakeEngine { Throw = new InvalidOperationException("disk exploded") };
        var tab = Vm.NewMain(engine, log: log).SelectedTab!;
        tab.LookIn = @"C:\data";

        await tab.StartCommand.ExecuteAsync(null);

        Assert.True(tab.IsError);
        Assert.Contains("disk exploded", tab.StatusText, StringComparison.Ordinal);
        Assert.False(tab.IsRunning);
        Assert.Contains("Search failed", File.ReadAllText(logPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadUserInputIsShownButNotLoggedAsAnError()
    {
        var log = NewLog(out var logPath);
        var engine = new FakeEngine { Throw = new FormatException("Missing ')' in search expression.") };
        var tab = Vm.NewMain(engine, log: log).SelectedTab!;
        tab.LookIn = @"C:\data";

        await tab.StartCommand.ExecuteAsync(null);

        Assert.True(tab.IsError);
        Assert.False(File.Exists(logPath));
    }

    // ---- folder pickers ----

    [Fact]
    public void BrowseReplacesTheFolderAndCancelKeepsIt()
    {
        var dialogs = new FakeDialogs { FolderAnswer = @"C:\picked" };
        var tab = Vm.NewMain(dialogs: dialogs).SelectedTab!;
        tab.LookIn = @"C:\old";

        tab.BrowseCommand.Execute(null);
        Assert.Equal(@"C:\picked", tab.LookIn);

        dialogs.FolderAnswer = null;
        tab.BrowseCommand.Execute(null);
        Assert.Equal(@"C:\picked", tab.LookIn);
    }

    [Fact]
    public void AddFolderAppendsWithASemicolon()
    {
        var dialogs = new FakeDialogs { FolderAnswer = @"C:\two" };
        var tab = Vm.NewMain(dialogs: dialogs).SelectedTab!;
        tab.LookIn = @"C:\one";

        tab.AddFolderCommand.Execute(null);

        Assert.Equal(@"C:\one;C:\two", tab.LookIn);
    }

    // ---- export ----

    private async Task<(MainViewModel Vm, SearchTabViewModel Tab, FakeDialogs Dialogs)> WithResults(AppLog? log = null)
    {
        var dialogs = new FakeDialogs();
        var vm = Vm.NewMain(new FakeEngine(Result()), dialogs, log);
        var tab = vm.SelectedTab!;
        tab.LookIn = @"C:\data";
        await tab.StartCommand.ExecuteAsync(null);
        return (vm, tab, dialogs);
    }

    [Fact]
    public void ExportWithoutResultsExplainsInsteadOfAsking()
    {
        var dialogs = new FakeDialogs { SaveFileAnswer = Path.Combine(_dir.Root, "x.txt") };
        var vm = Vm.NewMain(dialogs: dialogs);

        vm.ExportResults(vm.SelectedTab);

        Assert.Single(dialogs.Messages);
        Assert.Equal("info", dialogs.Messages[0].Kind);
        Assert.False(File.Exists(dialogs.SaveFileAnswer));
    }

    [Fact]
    public async Task ExportWritesATextReport()
    {
        var (vm, tab, dialogs) = await WithResults();
        dialogs.SaveFileAnswer = Path.Combine(_dir.Root, "report.txt");

        tab.ExportCommand.Execute(null);

        Assert.Contains(@"C:\data\a.txt", File.ReadAllText(dialogs.SaveFileAnswer), StringComparison.Ordinal);
        Assert.Empty(dialogs.Messages);
        Assert.NotNull(vm);
    }

    [Fact]
    public async Task ExportWritesCsvWithAByteOrderMark()
    {
        var (_, tab, dialogs) = await WithResults();
        dialogs.SaveFileAnswer = Path.Combine(_dir.Root, "report.csv");

        tab.ExportCommand.Execute(null);

        var bytes = File.ReadAllBytes(dialogs.SaveFileAnswer);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Contains("\"ALIAS, here\"", File.ReadAllText(dialogs.SaveFileAnswer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingTheExportDialogWritesNothing()
    {
        var (_, tab, dialogs) = await WithResults();
        dialogs.SaveFileAnswer = null;

        tab.ExportCommand.Execute(null);

        Assert.Empty(dialogs.Messages);
    }

    [Fact]
    public async Task ExportFailureIsShownAndLogged()
    {
        var log = NewLog(out var logPath);
        var (_, tab, dialogs) = await WithResults(log);
        dialogs.SaveFileAnswer = Path.Combine(_dir.Root, "no-such-folder", "report.txt");

        tab.ExportCommand.Execute(null);

        Assert.Equal("error", Assert.Single(dialogs.Messages).Kind);
        Assert.Contains("Could not export", File.ReadAllText(logPath), StringComparison.Ordinal);
    }

    // ---- saved searches ----

    [Fact]
    public void SavedSearchCanBeOpenedInANewTab()
    {
        var dialogs = new FakeDialogs { SaveFileAnswer = Path.Combine(_dir.Root, "s.fsearch") };
        var vm = Vm.NewMain(dialogs: dialogs);
        vm.SelectedTab!.FileName = "*.prg";
        vm.SelectedTab.ContainingText = "ALIAS";
        vm.SelectedTab.ContentMode = ContentMode.Boolean;

        vm.SaveDefinition();
        dialogs.OpenFileAnswer = dialogs.SaveFileAnswer;
        vm.OpenDefinition();

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Equal(("*.prg", "ALIAS", ContentMode.Boolean), (vm.SelectedTab.FileName, vm.SelectedTab.ContainingText, vm.SelectedTab.ContentMode));
        Assert.Equal("ALIAS", vm.SelectedTab.Title);
        Assert.Empty(dialogs.Messages);
    }

    [Fact]
    public void OpeningAnInvalidSavedSearchShowsAnErrorAndAddsNoTab()
    {
        var log = NewLog(out var logPath);
        var dialogs = new FakeDialogs { OpenFileAnswer = _dir.Write("bad.fsearch", "not json") };
        var vm = Vm.NewMain(dialogs: dialogs, log: log);

        vm.OpenDefinition();

        Assert.Single(vm.Tabs);
        Assert.Equal("error", Assert.Single(dialogs.Messages).Kind);
        Assert.Contains("Could not open saved search", File.ReadAllText(logPath), StringComparison.Ordinal);
    }

    [Fact]
    public void CancellingOpenOrSaveDoesNothing()
    {
        var dialogs = new FakeDialogs();
        var vm = Vm.NewMain(dialogs: dialogs);

        vm.OpenDefinition();
        vm.SaveDefinition();

        Assert.Single(vm.Tabs);
        Assert.Empty(dialogs.Messages);
    }

    [Fact]
    public void HelpShowsTheSearchSyntax()
    {
        var dialogs = new FakeDialogs();
        Vm.NewMain(dialogs: dialogs).ShowHelp();

        var (kind, title, message) = Assert.Single(dialogs.Messages);
        Assert.Equal("info", kind);
        Assert.Equal(HelpText.SearchSyntaxTitle, title);
        Assert.Contains("Boolean", message, StringComparison.Ordinal);
    }
}

public sealed class AppLogTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void WritesLevelMessageAndException()
    {
        var path = Path.Combine(_dir.Root, "logs", "a.log");
        var log = new AppLog(path);

        log.Info("hello");
        log.Warning("careful", new IOException("locked"));
        log.Error("broken", new InvalidOperationException("boom"));

        var text = File.ReadAllText(path);
        Assert.Contains("[INFO] hello", text, StringComparison.Ordinal);
        Assert.Contains("[WARN] careful", text, StringComparison.Ordinal);
        Assert.Contains("IOException: locked", text, StringComparison.Ordinal);
        Assert.Contains("[ERROR] broken", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledLogWritesNothing()
    {
        AppLog.Disabled.Error("ignored", new InvalidOperationException("x"));
        Assert.Empty(Directory.GetFiles(_dir.Root));
    }

    [Fact]
    public void LargeLogIsRotated()
    {
        var path = _dir.Write("big.log", new string('x', 1024 * 1024 + 10));
        var log = new AppLog(path);

        log.Info("fresh start");

        Assert.True(File.Exists(Path.ChangeExtension(path, ".old.log")));
        Assert.Contains("fresh start", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.True(new FileInfo(path).Length < 1024);
    }

    [Fact]
    public void LoggingNeverThrows()
    {
        var blocker = _dir.Write("file.txt", "x"); // a file where a folder is needed
        new AppLog(Path.Combine(blocker, "a.log")).Error("still fine");
    }

    [Fact]
    public async Task ConcurrentWritesAreAllRecorded()
    {
        var path = Path.Combine(_dir.Root, "c.log");
        var log = new AppLog(path);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => log.Info("line " + i))));

        Assert.Equal(50, File.ReadAllLines(path).Count(l => l.Contains("[INFO]", StringComparison.Ordinal)));
    }
}
