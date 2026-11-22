using FileSearch.App.Services;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

/// <summary>Records what the application asked the user, and answers with preset values.</summary>
internal sealed class FakeDialogs : IDialogService
{
    public List<(string Kind, string Title, string Message)> Messages { get; } = [];
    public string? FolderAnswer { get; set; }
    public string? OpenFileAnswer { get; set; }
    public string? SaveFileAnswer { get; set; }

    public void ShowInfo(string title, string message) => Messages.Add(("info", title, message));
    public void ShowWarning(string title, string message) => Messages.Add(("warning", title, message));
    public void ShowError(string title, string message) => Messages.Add(("error", title, message));
    public string? PickFolder(string title, string? initialFolder) => FolderAnswer;
    public string? PickOpenFile(string title, string filter) => OpenFileAnswer;
    public string? PickSaveFile(string title, string filter, string defaultExtension) => SaveFileAnswer;
}

/// <summary>A search engine that returns canned results without touching the file system.</summary>
internal sealed class FakeEngine(params FileResult[] results) : ISearchEngine
{
    public SearchCriteria? LastCriteria { get; private set; }
    public Exception? Throw { get; set; }

    public Task<SearchSummary> RunAsync(
        SearchCriteria criteria, Action<FileResult> onResult, IProgress<SearchProgress>? progress = null,
        PauseControl? pause = null, CancellationToken ct = default)
    {
        LastCriteria = criteria;
        if (Throw is not null) throw Throw;
        foreach (var r in results) onResult(r);
        return Task.FromResult(new SearchSummary(results.Length, results.Sum(r => r.Length), results.Length,
            results.Sum(r => r.Length), 0, SearchState.Completed, TimeSpan.FromSeconds(1)));
    }
}
internal sealed class TempDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fs-apptest-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Root);

    public string Write(string relative, string content)
    {
        var p = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
    }
}

internal static class Vm
{
    public static MainViewModel NewMain(ISearchEngine? engine = null, IDialogService? dialogs = null, AppLog? log = null) =>
        new(new AppSettings(), engine, dialogs, log);
}
