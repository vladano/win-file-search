using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSearch.App.Services;
using FileSearch.Core;

namespace FileSearch.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public AppSettings Settings { get; }
    public UiOptions Ui { get; } = new();
    public ObservableCollection<SearchTabViewModel> Tabs { get; } = [];

    /// <summary>Everything the application asks the user through dialogs.</summary>
    public IDialogService Dialogs { get; }

    /// <summary>The search implementation used by every tab.</summary>
    public ISearchEngine Engine { get; }

    public AppLog Log { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private SearchTabViewModel? _selectedTab;

    /// <param name="engine">Search implementation. Defaults to the real engine.</param>
    /// <param name="dialogs">Dialogs. Defaults to ones that never appear (the app passes the real ones).</param>
    /// <param name="log">Log. Defaults to a log that discards everything.</param>
    public MainViewModel(AppSettings settings, ISearchEngine? engine = null, IDialogService? dialogs = null, AppLog? log = null)
    {
        Settings = settings;
        Log = log ?? AppLog.Disabled;
        Engine = engine ?? new SearchEngine(Log);
        Dialogs = dialogs ?? new NullDialogService();

        Ui.Mode = settings.Mode;
        Ui.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiOptions.Mode)) Settings.Mode = Ui.Mode;
        };
        NewTab();
    }

    public string WindowTitle => SelectedTab is { HasSearched: true } t
        ? $"File Search - Search: {t.Title} in {t.LookIn} ({t.FileName})"
        : "File Search";

    partial void OnSelectedTabChanged(SearchTabViewModel? oldValue, SearchTabViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnTabPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SearchTabViewModel.Title) or nameof(SearchTabViewModel.HasSearched))
            OnPropertyChanged(nameof(WindowTitle));
    }

    [RelayCommand]
    private void AddTab() => NewTab();

    public SearchTabViewModel NewTab(string? lookIn = null)
    {
        var tab = new SearchTabViewModel(this);
        if (!string.IsNullOrEmpty(lookIn)) tab.LookIn = lookIn;
        else if (SelectedTab is not null) tab.LookIn = SelectedTab.LookIn;
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    [RelayCommand]
    private void CloseTab(SearchTabViewModel? tab)
    {
        if (tab is null) return;
        var idx = Tabs.IndexOf(tab);
        tab.Dispose();
        Tabs.Remove(tab);
        if (Tabs.Count == 0) NewTab();
        else if (SelectedTab == tab || SelectedTab is null) SelectedTab = Tabs[Math.Clamp(idx, 0, Tabs.Count - 1)];
    }

    /// <summary>Opens a tab for a command line / context menu request.</summary>
    public void OpenFromCommandLine(CommandLineOptions opts)
    {
        if (!opts.HasSearchRequest) return;

        // Reuse the initial pristine tab instead of leaving an empty one behind.
        var tab = Tabs.Count == 1 && SelectedTab is { IsPristine: true } p ? p : NewTab();
        SelectedTab = tab;
        if (opts.Folder is not null) tab.LookIn = opts.Folder;
        if (opts.Name is not null) tab.FileName = opts.Name;
        if (opts.Text is not null) tab.ContainingText = opts.Text;
        if (opts.Start) tab.StartCommand.Execute(null);
    }

    // ---- file and dialog flows (kept here so they can be tested without windows) ----

    /// <summary>Asks for a .fsearch file and opens it in a new tab.</summary>
    public void OpenDefinition()
    {
        const string title = "Open saved search";
        var path = Dialogs.PickOpenFile(title, SearchDefinition.FileFilter);
        if (path is null) return;
        try
        {
            var def = SearchDefinition.Load(path);
            var tab = NewTab();
            tab.ApplyDefinition(def);
            tab.Title = string.IsNullOrWhiteSpace(def.ContainingText) ? def.FileName : def.ContainingText;
        }
        catch (Exception ex)
        {
            Fail(title, $"Could not open saved search {path}", ex);
        }
    }

    /// <summary>Asks for a file name and saves the selected tab's criteria to it.</summary>
    public void SaveDefinition()
    {
        const string title = "Save search definition";
        if (SelectedTab is not { } tab) return;
        var path = Dialogs.PickSaveFile(title, SearchDefinition.FileFilter, ".fsearch");
        if (path is null) return;
        try
        {
            tab.ToDefinition().Save(path);
        }
        catch (Exception ex)
        {
            Fail(title, $"Could not save search definition {path}", ex);
        }
    }

    /// <summary>Exports all results of <paramref name="tab"/> to TXT or CSV, chosen by the file extension.</summary>
    public void ExportResults(SearchTabViewModel? tab)
    {
        const string title = "Export";
        if (tab is null) return;
        if (tab.Results.Count == 0)
        {
            Dialogs.ShowInfo(title, "There are no results to export.");
            return;
        }

        var path = Dialogs.PickSaveFile("Export results", "Text report (*.txt)|*.txt|CSV (*.csv)|*.csv", ".txt");
        if (path is null) return;
        try
        {
            var csv = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            var text = csv ? ReportBuilder.ToCsv(tab.AllResults) : ReportBuilder.ToText(tab.AllResults);
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: csv));
        }
        catch (Exception ex)
        {
            Fail(title, $"Could not export results to {path}", ex);
        }
    }

    public void ShowHelp() => Dialogs.ShowInfo(HelpText.SearchSyntaxTitle, HelpText.SearchSyntax);

    /// <summary>Logs the failure and tells the user the reason.</summary>
    internal void Fail(string title, string logMessage, Exception ex)
    {
        Log.Error(logMessage, ex);
        Dialogs.ShowError(title, ex.Message);
    }

    public void Shutdown()
    {
        foreach (var t in Tabs) t.Dispose();
        Settings.Save();
    }
}
