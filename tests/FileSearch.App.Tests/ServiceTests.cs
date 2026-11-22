using System.Collections.ObjectModel;
using FileSearch.App.Services;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

public class CommandLineOptionsTests
{
    [Fact]
    public void NoArgumentsMeansNoSearchRequest()
    {
        var o = CommandLineOptions.Parse([]);
        Assert.False(o.HasSearchRequest);
        Assert.False(o.Start);
        Assert.Null(o.Folder);
    }

    [Fact]
    public void FolderOnly()
    {
        var o = CommandLineOptions.Parse([@"C:\Projects"]);
        Assert.Equal(@"C:\Projects", o.Folder);
        Assert.True(o.HasSearchRequest);
    }

    [Fact]
    public void AllOptions()
    {
        var o = CommandLineOptions.Parse([@"C:\Projects", "--name", "*.cs", "--text", "ALIAS", "--start"]);
        Assert.Equal(("*.cs", "ALIAS", true), (o.Name, o.Text, o.Start));
        Assert.Equal(@"C:\Projects", o.Folder);
    }

    [Fact]
    public void OptionsAreCaseInsensitiveAndOrderIndependent()
    {
        var o = CommandLineOptions.Parse(["--START", "--Text", "x", @"D:\data"]);
        Assert.True(o.Start);
        Assert.Equal("x", o.Text);
        Assert.Equal(@"D:\data", o.Folder);
    }

    [Fact]
    public void NameOrTextWithoutFolderIsStillARequest()
    {
        Assert.True(CommandLineOptions.Parse(["--text", "x"]).HasSearchRequest);
        Assert.True(CommandLineOptions.Parse(["--name", "*.cs"]).HasSearchRequest);
    }

    [Fact]
    public void ValuelessTrailingOptionIsIgnored()
    {
        var o = CommandLineOptions.Parse([@"C:\x", "--text"]);
        Assert.Null(o.Text);
        Assert.Equal(@"C:\x", o.Folder);
    }

    [Fact]
    public void ShellSwitches()
    {
        Assert.True(CommandLineOptions.Parse(["--install-shell"]).InstallShell);
        Assert.True(CommandLineOptions.Parse(["--uninstall-shell"]).UninstallShell);
        Assert.False(CommandLineOptions.Parse(["--install-shell"]).HasSearchRequest);
    }

    [Fact]
    public void UnknownSwitchesAreNotTreatedAsFolders() =>
        Assert.Null(CommandLineOptions.Parse(["--bogus"]).Folder);

    [Theory]
    [InlineData("C:\"", "C:\\")]      // what the CLR produces for "C:\" passed by Explorer
    [InlineData("C:", "C:\\")]
    [InlineData("\"C:\\Program Files\"", "C:\\Program Files")]
    [InlineData("  C:\\x  ", "C:\\x")]
    public void FolderPathsAreNormalized(string raw, string expected) =>
        Assert.Equal(expected, CommandLineOptions.Parse([raw]).Folder);

    [Fact]
    public void AFilePathIsReplacedByItsFolder()
    {
        using var dir = new TempDir();
        var file = dir.Write("a.txt", "x");
        Assert.Equal(dir.Root, CommandLineOptions.Parse([file]).Folder);
    }
}

public class AppSettingsTests
{
    [Fact]
    public void RememberInsertsNewestFirst()
    {
        var list = new ObservableCollection<string>();
        AppSettings.Remember(list, "a");
        AppSettings.Remember(list, "b");
        Assert.Equal(["b", "a"], list);
    }

    [Fact]
    public void RememberMovesDuplicatesToTheTop()
    {
        var list = new ObservableCollection<string> { "c", "b", "a" };
        AppSettings.Remember(list, "a");
        Assert.Equal(["a", "c", "b"], list);
    }

    [Fact]
    public void RememberKeepsTopEntryInPlace()
    {
        var list = new ObservableCollection<string> { "a", "b" };
        var changes = 0;
        list.CollectionChanged += (_, _) => changes++;
        AppSettings.Remember(list, "a");
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RememberIgnoresBlank(string? value)
    {
        var list = new ObservableCollection<string> { "a" };
        AppSettings.Remember(list, value);
        Assert.Equal(["a"], list);
    }

    [Fact]
    public void RememberTrimsToTheLimit()
    {
        var list = new ObservableCollection<string>();
        for (var i = 0; i < 40; i++) AppSettings.Remember(list, "item" + i, max: 25);
        Assert.Equal(25, list.Count);
        Assert.Equal("item39", list[0]);
        Assert.DoesNotContain("item0", list);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Root, "nested", "settings.json");
        var s = new AppSettings { Mode = UiMode.Simple, Left = 10, Top = 20, Width = 800, Height = 600, Maximized = true };
        s.NameHistory.Add("*.cs");
        s.TextHistory.Add("ALIAS");
        s.FolderHistory.Add(@"C:\x");

        s.Save(path);
        var loaded = AppSettings.Load(path);

        Assert.Equal(UiMode.Simple, loaded.Mode);
        Assert.Equal((10, 20, 800, 600), (loaded.Left, loaded.Top, loaded.Width, loaded.Height));
        Assert.True(loaded.Maximized);
        Assert.Equal(["*.cs"], loaded.NameHistory);
        Assert.Equal(["ALIAS"], loaded.TextHistory);
        Assert.Equal([@"C:\x"], loaded.FolderHistory);
    }

    [Fact]
    public void LoadOfMissingFileGivesDefaults()
    {
        using var dir = new TempDir();
        var s = AppSettings.Load(Path.Combine(dir.Root, "none.json"));
        Assert.Equal(UiMode.Expert, s.Mode); // Expert by default
        Assert.Empty(s.NameHistory);
    }

    [Fact]
    public void LoadOfCorruptFileGivesDefaults()
    {
        using var dir = new TempDir();
        var path = dir.Write("settings.json", "{ this is not json");
        Assert.Equal(UiMode.Expert, AppSettings.Load(path).Mode);
    }

    [Fact]
    public void SaveToAnUnwritablePathDoesNotThrow()
    {
        using var dir = new TempDir();
        var blocker = dir.Write("file.txt", "x"); // a file where a directory is needed
        new AppSettings().Save(Path.Combine(blocker, "settings.json"));
    }
}

public class SearchDefinitionTests
{
    [Fact]
    public void RoundTripKeepsEveryField()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Root, "x.fsearch");
        var d = new SearchDefinition
        {
            FileName = "*.prg", ContainingText = "ALIAS", LookIn = @"C:\a;C:\b", Subfolders = false,
            NameMatchCase = true, ContentMatchCase = true, NameMode = NameMode.Regex, ContentMode = ContentMode.Boolean,
            MinSize = "5", MaxSize = "50", SearchDocuments = false, ExcludeFolders = ".git",
            ModifiedAfter = new DateTime(2024, 1, 2), ModifiedBefore = new DateTime(2024, 1, 3),
            CreatedAfter = new DateTime(2023, 1, 1), CreatedBefore = new DateTime(2023, 2, 1),
            AccessedAfter = new DateTime(2022, 1, 1), AccessedBefore = new DateTime(2022, 2, 1),
        };

        d.Save(path);
        var l = SearchDefinition.Load(path);

        Assert.Equivalent(d, l);
    }

    [Fact]
    public void LoadOfInvalidJsonThrows()
    {
        using var dir = new TempDir();
        var path = dir.Write("bad.fsearch", "not json");
        Assert.ThrowsAny<Exception>(() => SearchDefinition.Load(path));
    }

    [Fact]
    public void LoadOfMissingFieldsUsesDefaults()
    {
        using var dir = new TempDir();
        var path = dir.Write("min.fsearch", "{}");
        var d = SearchDefinition.Load(path);
        Assert.Equal("*", d.FileName);
        Assert.True(d.Subfolders);
        Assert.True(d.SearchDocuments);
    }
}
