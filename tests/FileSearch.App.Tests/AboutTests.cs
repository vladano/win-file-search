using System.Windows.Documents;
using FileSearch.App.Services;

namespace FileSearch.App.Tests;

public class AppInfoTests
{
    [Theory]
    [InlineData("0.1.1+bd76b36624c22d30c1ee8ac8b6c0df0726ad9087", "0.1.1", "bd76b36")]
    [InlineData("0.1.1+abc", "0.1.1", "abc")]
    [InlineData("0.1.1+", "0.1.1", null)]
    [InlineData("0.1.1", "0.1.1", null)]
    [InlineData("1.0.0-beta.2+1234567890", "1.0.0-beta.2", "1234567")]
    public void SplitsVersionAndCommit(string informational, string version, string? commit) =>
        Assert.Equal((version, commit), AppInfo.SplitVersion(informational));

    [Fact]
    public void VersionTextShowsTheCommitWhenThereIsOne()
    {
        Assert.Equal("0.1.1 (commit bd76b36)", new AppInfo("File Search", "0.1.1", "bd76b36", "").VersionText);
        Assert.Equal("0.1.1", new AppInfo("File Search", "0.1.1", null, "").VersionText);
    }

    [Fact]
    public void ReadsTheRealAssembly()
    {
        var info = AppInfo.FromAssembly(typeof(App).Assembly);

        Assert.Equal("File Search", info.Product);
        Assert.Matches(@"^\d+\.\d+\.\d+", info.Version);
        Assert.Contains("MIT License", info.Copyright);
        Assert.Contains(AppInfo.RepositoryUrl, info.Copyright);
    }

    [Fact]
    public void LinksPointToTheGitHubRepository()
    {
        string[] urls = [AppInfo.RepositoryUrl, AppInfo.ManualUrl, AppInfo.IssuesUrl, AppInfo.ReleasesUrl, AppInfo.LicenseUrl];
        Assert.All(urls, u =>
        {
            var uri = new Uri(u);
            Assert.Equal("https", uri.Scheme);
            Assert.StartsWith("https://github.com/vladano/win-file-search", u);
        });
    }

    [Fact]
    public void LinkedRepositoryFilesExist()
    {
        // The manual and license links point at files on main; renaming them would break the links.
        var root = RepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "docs", "USER-MANUAL.md")));
        Assert.True(File.Exists(Path.Combine(root, "LICENSE")));
        Assert.EndsWith("/blob/main/docs/USER-MANUAL.md", AppInfo.ManualUrl);
        Assert.EndsWith("/blob/main/LICENSE", AppInfo.LicenseUrl);
    }

    [Fact]
    public void DetailsContainWhatABugReportNeeds()
    {
        var details = new AppInfo("File Search", "0.1.1", "bd76b36", "").Details();
        Assert.Contains("File Search 0.1.1 (commit bd76b36)", details);
        Assert.Contains("Windows:", details);
        Assert.Contains(".NET:", details);
        Assert.Contains(AppInfo.RepositoryUrl, details);
    }

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FileSearch.sln"))) return dir.FullName;
        throw new FileNotFoundException("FileSearch.sln not found above " + AppContext.BaseDirectory);
    }
}

public class AboutWindowTests
{
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new AggregateException(error);
        return result;
    }

    private static readonly AppInfo Info = new("File Search", "0.1.1", "bd76b36", "Copyright (c) 2026 vladano. MIT License.");

    [Fact]
    public void ShowsProductVersionAndCopyright()
    {
        var texts = OnSta(() =>
        {
            var w = new AboutWindow(Info);
            var all = DependencyObjectWalker.Texts(w);
            w.Close();
            return all;
        });

        Assert.Contains("File Search", texts);
        Assert.Contains("Version 0.1.1 (commit bd76b36)", texts);
        Assert.Contains("Copyright (c) 2026 vladano. MIT License.", texts);
        Assert.Contains(AppInfo.RepositoryUrl, texts);
    }

    [Fact]
    public void EveryLinkOpensItsAddress()
    {
        var opened = OnSta(() =>
        {
            var urls = new List<string>();
            var w = new AboutWindow(Info, urls.Add);
            foreach (var link in DependencyObjectWalker.Hyperlinks(w))
            {
                // The same path as a real click: Invoke raises Click, which raises RequestNavigate.
                var peer = new System.Windows.Automation.Peers.HyperlinkAutomationPeer(link);
                ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(
                    System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
            }
            w.Close();
            return urls;
        });

        Assert.Equal(
            [AppInfo.RepositoryUrl, AppInfo.ManualUrl, AppInfo.ReleasesUrl, AppInfo.IssuesUrl, AppInfo.LicenseUrl],
            opened);
    }

    [Fact]
    public void DetailsTextIsTheAppInfoDetails() =>
        Assert.Equal(Info.Details(), OnSta(() => new AboutWindow(Info).DetailsText));
}

/// <summary>Finds text and hyperlinks in a window's logical tree (it does not need to be shown).</summary>
internal static class DependencyObjectWalker
{
    public static List<string> Texts(System.Windows.DependencyObject root) =>
        All(root).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();

    public static List<Hyperlink> Hyperlinks(System.Windows.DependencyObject root)
    {
        var links = new List<Hyperlink>();
        foreach (var tb in All(root).OfType<System.Windows.Controls.TextBlock>())
            links.AddRange(tb.Inlines.OfType<Hyperlink>());
        return links;
    }

    private static IEnumerable<System.Windows.DependencyObject> All(System.Windows.DependencyObject node)
    {
        yield return node;
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<System.Windows.DependencyObject>())
            foreach (var d in All(child))
                yield return d;
    }
}
