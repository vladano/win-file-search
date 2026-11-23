using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

/// <summary>
/// Selecting a file in the results list shows its matching lines: the details pane switches to the Hits tab.
/// </summary>
public class DetailTabTests
{
    private static FileResult File(string name, int hits) =>
        new($@"C:\data\{name}", name, @"C:\data", 100, new DateTime(2024, 1, 1),
            Enumerable.Range(1, hits).Select(i => new Hit(i, "ALIAS", [new MatchRange(0, 5)])).ToArray());

    /// <summary>A tab that has run a (fake) search returning the given files.</summary>
    private static async Task<SearchTabViewModel> SearchedTab(params FileResult[] results)
    {
        var tab = Vm.NewMain(new FakeEngine(results)).SelectedTab!;
        tab.LookIn = Path.GetTempPath();
        tab.ContainingText = "ALIAS";
        await tab.StartCommand.ExecuteAsync(null);
        Assert.Equal(results.Length, tab.Results.Count);
        return tab;
    }

    private static ResultItem Row(SearchTabViewModel tab, string name) => tab.Results.Single(r => r.Name == name);

    [Fact]
    public void StartsOnSummary() =>
        Assert.Equal((int)DetailTab.Summary, Vm.NewMain().SelectedTab!.SelectedDetailTab);

    [Fact]
    public async Task SearchingDoesNotSwitchByItself()
    {
        var tab = await SearchedTab(File("a.prg", 2));
        Assert.Equal((int)DetailTab.Summary, tab.SelectedDetailTab); // the summary is what you want right after a search
    }

    [Fact]
    public async Task SelectingAFileWithHitsOpensHits()
    {
        var tab = await SearchedTab(File("a.prg", 2), File("b.prg", 1));

        tab.SelectedResult = Row(tab, "a.prg");

        Assert.Equal((int)DetailTab.Hits, tab.SelectedDetailTab);
    }

    [Fact]
    public async Task EveryNewSelectionOpensHitsAgain()
    {
        var tab = await SearchedTab(File("a.prg", 2), File("b.prg", 1));
        tab.SelectedResult = Row(tab, "a.prg");

        tab.SelectedDetailTab = (int)DetailTab.Summary; // the user looks at the summary...
        tab.SelectedResult = Row(tab, "b.prg");         // ...then clicks another file

        Assert.Equal((int)DetailTab.Hits, tab.SelectedDetailTab);
    }

    [Fact]
    public async Task ClickingTheSelectedFileAgainOpensHits()
    {
        var tab = await SearchedTab(File("a.prg", 2));
        tab.SelectedResult = Row(tab, "a.prg");
        tab.SelectedDetailTab = (int)DetailTab.Reports;

        tab.ShowHitsOfSelectedResult(); // what a click on the already-selected row calls

        Assert.Equal((int)DetailTab.Hits, tab.SelectedDetailTab);
    }

    [Fact]
    public async Task FilesWithoutHitsKeepTheCurrentTab()
    {
        // Name-only and NOT searches list files without matching lines; an empty Hits tab would not help.
        var tab = await SearchedTab(File("a.prg", 0));
        tab.SelectedDetailTab = (int)DetailTab.Reports;

        tab.SelectedResult = Row(tab, "a.prg");

        Assert.Equal((int)DetailTab.Reports, tab.SelectedDetailTab);
    }

    [Fact]
    public async Task ClearingTheSelectionKeepsTheCurrentTab()
    {
        var tab = await SearchedTab(File("a.prg", 1));
        tab.SelectedResult = Row(tab, "a.prg");

        tab.SelectedResult = null;

        Assert.Equal((int)DetailTab.Hits, tab.SelectedDetailTab);
    }

    [Fact]
    public void ShowHitsWithoutASelectionDoesNothing()
    {
        var tab = Vm.NewMain().SelectedTab!;
        tab.ShowHitsOfSelectedResult();
        Assert.Equal((int)DetailTab.Summary, tab.SelectedDetailTab);
    }

    [Fact]
    public async Task EachSearchTabRemembersItsOwnDetailTab()
    {
        var main = Vm.NewMain(new FakeEngine(File("a.prg", 1)));
        var first = main.SelectedTab!;
        first.LookIn = Path.GetTempPath();
        first.ContainingText = "ALIAS";
        await first.StartCommand.ExecuteAsync(null);
        first.SelectedResult = first.Results[0];

        var second = main.NewTab();

        Assert.Equal((int)DetailTab.Hits, first.SelectedDetailTab);
        Assert.Equal((int)DetailTab.Summary, second.SelectedDetailTab);
    }

    // ---- the binding in SearchTabView.xaml ----

    private static void OnSta(Func<Task> work)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { work().GetAwaiter().GetResult(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void TabControlFollowsTheViewModelBothWays() => OnSta(async () =>
    {
        var tab = await SearchedTab(File("a.prg", 1));
        var tabs = new TabControl { DataContext = tab };
        foreach (var header in Enum.GetNames<DetailTab>()) tabs.Items.Add(new TabItem { Header = header });
        tabs.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty,
            new Binding(nameof(SearchTabViewModel.SelectedDetailTab)));

        Assert.Equal("Summary", ((TabItem)tabs.SelectedItem).Header);

        tab.SelectedResult = tab.Results[0];
        Assert.Equal("Hits", ((TabItem)tabs.SelectedItem).Header);

        tabs.SelectedIndex = (int)DetailTab.Reports; // the user clicks a tab
        Assert.Equal((int)DetailTab.Reports, tab.SelectedDetailTab);
    });

    [Fact]
    public void XamlTabOrderMatchesTheEnum()
    {
        // SelectedIndex is a position, so the TabItems in the details pane must stay in DetailTab order.
        var xaml = XDocument.Load(FindRepoFile("src", "FileSearch.App", "SearchTabView.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var details = Assert.Single(xaml.Descendants(p + "TabControl"),
            t => (string?)t.Attribute("SelectedIndex") == "{Binding SelectedDetailTab}");
        var headers = details.Elements(p + "TabItem").Select(t => (string?)t.Attribute("Header")).ToArray();

        Assert.Equal(Enum.GetNames<DetailTab>(), headers);
    }

    private static string FindRepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "FileSearch.sln")))
                return Path.Combine([dir.FullName, .. parts]);
        }
        throw new FileNotFoundException("Repository root (FileSearch.sln) not found above " + AppContext.BaseDirectory);
    }
}
