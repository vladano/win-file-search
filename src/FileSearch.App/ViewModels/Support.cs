using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileSearch.Core;

namespace FileSearch.App.ViewModels;

/// <summary>ObservableCollection that can add many items with a single change notification.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IReadOnlyCollection<T> items)
    {
        if (items.Count == 0) return;
        foreach (var i in items) Items.Add(i);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>Row in the results list.</summary>
public sealed class ResultItem(FileResult source)
{
    public FileResult Source { get; } = source;
    public string Name => Source.Name;
    public string Directory => Source.Directory;
    public string FullPath => Source.FullPath;
    public double SizeKb => Source.Length / 1024.0;
    public DateTime Modified => Source.Modified;
    public int HitCount => Source.Hits.Count;
    public IReadOnlyList<Hit> Hits => Source.Hits;
}

/// <summary>How much of the search form is shown.</summary>
public enum UiMode { Simple, Expert }

/// <summary>A value shown in a combo box with its display label. Bind with SelectedValuePath="Value".</summary>
public sealed record ModeOption<T>(T Value, string Label) where T : struct, Enum;

/// <summary>View-wide options shared by all tabs (Simple/Expert).</summary>
public partial class UiOptions : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpert))]
    private UiMode _mode = UiMode.Expert;

    public bool IsExpert => Mode == UiMode.Expert;
}

/// <summary>Timings and limits of the user interface, in one place.</summary>
internal static class UiTuning
{
    /// <summary>How often results found by the engine are moved into the list.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Most results added to the list per flush, so one tick cannot freeze the UI.</summary>
    public const int FlushBatchSize = 2000;

    /// <summary>Files shown in the Reports tab. The export always contains everything.</summary>
    public const int ReportFileLimit = 2000;

    /// <summary>Entries kept in each history list.</summary>
    public const int HistoryLimit = 25;
}
/// <summary>An optional date bound: a checkbox plus a date picker kept in sync.</summary>
public partial class DateFilter : ObservableObject
{
    [ObservableProperty] private bool _use;
    [ObservableProperty] private DateTime? _value;

    partial void OnUseChanged(bool value)
    {
        if (value && Value is null) Value = DateTime.Today;
        else if (!value && Value is not null) Value = null;
    }

    partial void OnValueChanged(DateTime? value) => Use = value.HasValue;

    public void Clear() => Value = null;

    public DateTime? AfterBound => Value?.Date;
    public DateTime? BeforeBound => Value?.Date.AddDays(1).AddTicks(-1);
}
