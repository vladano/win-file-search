using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSearch.App.Services;
using FileSearch.Core;

namespace FileSearch.App.ViewModels;

public partial class SearchTabViewModel : ObservableObject, IDisposable
{
    public const string NewSearchTitle = "New search";

    private readonly MainViewModel _owner;
    private readonly SearchSession _session;

    public SearchTabViewModel(MainViewModel owner)
    {
        _owner = owner;
        _session = new SearchSession(owner.Engine, AddResults, OnProgress);
    }

    public UiOptions Ui => _owner.Ui;
    public AppSettings Settings => _owner.Settings;

    public IReadOnlyList<ModeOption<NameMode>> NameModes { get; } =
    [
        new(NameMode.Dos, "DOS Expression"),
        new(NameMode.Regex, "Regular expression"),
    ];

    public IReadOnlyList<ModeOption<ContentMode>> ContentModes { get; } =
    [
        new(ContentMode.Plain, "Plain text"),
        new(ContentMode.Boolean, "Boolean"),
        new(ContentMode.Regex, "Regular expression"),
    ];

    public IReadOnlyList<ModeOption<UiMode>> Modes { get; } =
    [
        new(UiMode.Simple, "Simple"),
        new(UiMode.Expert, "Expert"),
    ];
    // ---- criteria ----
    [ObservableProperty] private string _fileName = "*";
    [ObservableProperty] private string _containingText = "";
    [ObservableProperty] private string _lookIn = "";
    [ObservableProperty] private bool _subfolders = true;
    [ObservableProperty] private bool _nameMatchCase;
    [ObservableProperty] private bool _contentMatchCase;
    [ObservableProperty] private NameMode _nameMode = NameMode.Dos;
    [ObservableProperty] private ContentMode _contentMode = ContentMode.Plain;
    [ObservableProperty] private string _minSize = "";
    [ObservableProperty] private string _maxSize = "";
    [ObservableProperty] private bool _searchDocuments = true;
    [ObservableProperty] private string _excludeFolders = "";

    public DateFilter ModifiedAfter { get; } = new();
    public DateFilter ModifiedBefore { get; } = new();
    public DateFilter CreatedAfter { get; } = new();
    public DateFilter CreatedBefore { get; } = new();
    public DateFilter AccessedAfter { get; } = new();
    public DateFilter AccessedBefore { get; } = new();

    // ---- state ----
    [ObservableProperty] private string _title = NewSearchTitle;
    [ObservableProperty] private bool _hasSearched;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    private bool _isRunning;

    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _foundText = "0 items (0.00 KB)";
    [ObservableProperty] private string _checkedText = "0 items (0.00 KB)";
    [ObservableProperty] private string _skippedText = "";
    [ObservableProperty] private string _reportText = "";
    [ObservableProperty] private ResultItem? _selectedResult;

    public BulkObservableCollection<ResultItem> Results { get; } = [];

    /// <summary>True while the tab is untouched, so a command-line request can reuse it.</summary>
    public bool IsPristine => !HasSearched && string.IsNullOrEmpty(ContainingText) && string.IsNullOrEmpty(LookIn);

    // ---- commands ----

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        SearchCriteria criteria;
        try
        {
            criteria = BuildCriteria();
        }
        catch (FormatException ex)
        {
            SetError(ex.Message);
            return;
        }

        RememberHistory();

        Results.Clear();
        SelectedResult = null;
        ReportText = "";
        IsError = false;
        FoundText = CheckedText = FormatCount(0, 0);
        SkippedText = "";
        StatusText = "Searching...";
        Title = !string.IsNullOrWhiteSpace(ContainingText) ? ContainingText.Trim() : FileName;
        HasSearched = true;
        IsRunning = true;
        IsPaused = false;

        try
        {
            var summary = await _session.RunAsync(criteria);
            FoundText = FormatCount(summary.Found, summary.FoundBytes);
            CheckedText = FormatCount(summary.Checked, summary.CheckedBytes);
            SkippedText = summary.Skipped > 0 ? $"{summary.Skipped:N0} (binary, too large or unreadable)" : "";
            StatusText = summary.State == SearchState.Cancelled
                ? $"Cancelled after {summary.Elapsed.TotalSeconds:N1} s"
                : $"Finished in {summary.Elapsed.TotalSeconds:N1} s";
            ReportText = BuildReport();
        }
        catch (Exception ex)
        {
            // Invalid input (bad regular expression, missing folder) is expected; anything else is worth a log entry.
            if (ex is not (ArgumentException or FormatException or DirectoryNotFoundException))
                _owner.Log.Error("Search failed", ex);
            SetError(ex.Message);
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
        }
    }
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _session.Stop();

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Pause() => IsPaused = _session.TogglePause();

    [RelayCommand]
    private void Browse()
    {
        var folders = SearchCriteria.SplitList(LookIn);
        var first = folders.Count > 0 ? folders[0] : null;
        if (_owner.Dialogs.PickFolder("Look in", first) is { } folder) LookIn = folder;
    }

    [RelayCommand]
    private void AddFolder()
    {
        if (_owner.Dialogs.PickFolder("Add folder", null) is not { } folder) return;
        LookIn = string.IsNullOrWhiteSpace(LookIn) ? folder : LookIn.TrimEnd(';', ' ') + ";" + folder;
    }

    /// <summary>Exports this tab's results (asks for a file name).</summary>
    [RelayCommand]
    private void Export() => _owner.ExportResults(this);

    /// <summary>Shows the search syntax help.</summary>
    [RelayCommand]
    private void ShowHelp() => _owner.ShowHelp();
    [RelayCommand]
    private void ClearDates()
    {
        foreach (var d in new[] { ModifiedAfter, ModifiedBefore, CreatedAfter, CreatedBefore, AccessedAfter, AccessedBefore })
            d.Clear();
    }

    // ---- definitions ----

    public SearchDefinition ToDefinition() => new()
    {
        FileName = FileName,
        ContainingText = ContainingText,
        LookIn = LookIn,
        Subfolders = Subfolders,
        NameMatchCase = NameMatchCase,
        ContentMatchCase = ContentMatchCase,
        NameMode = NameMode,
        ContentMode = ContentMode,
        MinSize = MinSize,
        MaxSize = MaxSize,
        SearchDocuments = SearchDocuments,
        ExcludeFolders = ExcludeFolders,
        ModifiedAfter = ModifiedAfter.Value,
        ModifiedBefore = ModifiedBefore.Value,
        CreatedAfter = CreatedAfter.Value,
        CreatedBefore = CreatedBefore.Value,
        AccessedAfter = AccessedAfter.Value,
        AccessedBefore = AccessedBefore.Value,
    };

    public void ApplyDefinition(SearchDefinition d)
    {
        FileName = d.FileName;
        ContainingText = d.ContainingText;
        LookIn = d.LookIn;
        Subfolders = d.Subfolders;
        NameMatchCase = d.NameMatchCase;
        ContentMatchCase = d.ContentMatchCase;
        NameMode = Enum.IsDefined(d.NameMode) ? d.NameMode : NameMode.Dos;
        ContentMode = Enum.IsDefined(d.ContentMode) ? d.ContentMode : ContentMode.Plain;
        MinSize = d.MinSize;
        MaxSize = d.MaxSize;
        SearchDocuments = d.SearchDocuments;
        ExcludeFolders = d.ExcludeFolders;
        ModifiedAfter.Value = d.ModifiedAfter;
        ModifiedBefore.Value = d.ModifiedBefore;
        CreatedAfter.Value = d.CreatedAfter;
        CreatedBefore.Value = d.CreatedBefore;
        AccessedAfter.Value = d.AccessedAfter;
        AccessedBefore.Value = d.AccessedBefore;
    }

    /// <summary>All results found so far, for export.</summary>
    public IEnumerable<FileResult> AllResults => Results.Select(r => r.Source);

    // ---- internals ----

    private SearchCriteria BuildCriteria()
    {
        var folders = SearchCriteria.SplitList(LookIn)
            .Select(f => Environment.ExpandEnvironmentVariables(f.Trim('"')))
            .ToList();
        if (folders.Count == 0) throw new FormatException("Enter a folder in \"Look in\".");

        DateRange Range(DateFilter after, DateFilter before) =>
            new() { After = after.AfterBound, Before = before.BeforeBound };

        return new SearchCriteria
        {
            Folders = folders,
            FileName = FileName,
            NameMode = NameMode,
            NameMatchCase = NameMatchCase,
            ContainingText = ContainingText,
            ContentMode = ContentMode,
            ContentMatchCase = ContentMatchCase,
            Subfolders = Subfolders,
            MinSizeKb = ParseKb(MinSize),
            MaxSizeKb = ParseKb(MaxSize),
            Modified = Range(ModifiedAfter, ModifiedBefore),
            Created = Range(CreatedAfter, CreatedBefore),
            Accessed = Range(AccessedAfter, AccessedBefore),
            SearchDocuments = SearchDocuments,
            ExcludeFolders = SearchCriteria.SplitList(ExcludeFolders, allowComma: true),
        };
    }

    private static long? ParseKb(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (long.TryParse(text.Trim(), out var v) && v >= 0) return v;
        throw new FormatException("Size must be a whole number of kB.");
    }

    private void RememberHistory()
    {
        // Updating the history can reset editable combo boxes, so restore the typed values afterwards.
        var (name, text, look) = (FileName, ContainingText, LookIn);
        AppSettings.Remember(Settings.NameHistory, name);
        AppSettings.Remember(Settings.TextHistory, text);
        AppSettings.Remember(Settings.FolderHistory, look);
        FileName = name;
        ContainingText = text;
        LookIn = look;
        Settings.Save();
    }

    private void OnProgress(SearchProgress p)
    {
        if (!IsRunning) return;
        FoundText = FormatCount(p.Found, p.FoundBytes);
        CheckedText = FormatCount(p.Checked, p.CheckedBytes);
        StatusText = (p.State == SearchState.Paused ? "Paused" : "Searching...") + $" {p.Elapsed.TotalSeconds:N0} s";
    }

    private void AddResults(IReadOnlyList<FileResult> batch) =>
        Results.AddRange(batch.Select(r => new ResultItem(r)).ToList());
    private string BuildReport()
    {
        var text = ReportBuilder.ToText(AllResults.Take(UiTuning.ReportFileLimit));
        return Results.Count > UiTuning.ReportFileLimit
            ? text + $"\n... {Results.Count - UiTuning.ReportFileLimit:N0} more files. Use Export to save the full report.\n"
            : text;
    }

    /// <summary>Reports a failure to open a file or folder from the results list.</summary>
    public void ShowLaunchError(Exception ex)
    {
        _owner.Log.Warning("Could not open result", ex);
        _owner.Dialogs.ShowWarning("File Search", ex.Message);
    }

    private void SetError(string message)
    {
        IsError = true;
        StatusText = "Error: " + message;
    }

    private static string FormatCount(long items, long bytes) => $"{items:N0} items ({bytes / 1024.0:N2} KB)";

    public void Dispose()
    {
        _session.Dispose();
        GC.SuppressFinalize(this);
    }
}
