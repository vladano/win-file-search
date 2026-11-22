using System.Collections.Specialized;
using System.ComponentModel;
using FileSearch.App.Services;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

public class SupportTypesTests
{
    [Fact]
    public void ModeDrivesIsExpertAndNotifies()
    {
        var ui = new UiOptions();
        var changed = new List<string?>();
        ui.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(ui.IsExpert); // Expert by default
        ui.Mode = UiMode.Simple;

        Assert.False(ui.IsExpert);
        Assert.Contains(nameof(UiOptions.IsExpert), changed);
    }

    [Fact]
    public void AddRangeRaisesASingleReset()
    {
        var c = new BulkObservableCollection<int>();
        var events = new List<NotifyCollectionChangedAction>();
        c.CollectionChanged += (_, e) => events.Add(e.Action);

        c.AddRange([1, 2, 3]);

        Assert.Equal([NotifyCollectionChangedAction.Reset], events);
        Assert.Equal([1, 2, 3], c);
    }

    [Fact]
    public void AddRangeOfNothingRaisesNothing()
    {
        var c = new BulkObservableCollection<int>();
        var raised = false;
        c.CollectionChanged += (_, _) => raised = true;
        c.AddRange([]);
        Assert.False(raised);
    }

    [Fact]
    public void AddRangeNotifiesCount()
    {
        var c = new BulkObservableCollection<int>();
        var names = new List<string?>();
        ((INotifyPropertyChanged)c).PropertyChanged += (_, e) => names.Add(e.PropertyName);
        c.AddRange([1]);
        Assert.Contains("Count", names);
    }

    [Fact]
    public void ResultItemExposesTheUnderlyingResult()
    {
        var hits = new[] { new Hit(1, "x", []) };
        var r = new FileResult(@"C:\d\a.txt", "a.txt", @"C:\d", 2048, new DateTime(2024, 1, 1), hits);
        var item = new ResultItem(r);

        Assert.Equal(("a.txt", @"C:\d", @"C:\d\a.txt"), (item.Name, item.Directory, item.FullPath));
        Assert.Equal(2.0, item.SizeKb);
        Assert.Equal(1, item.HitCount);
        Assert.Same(r, item.Source);
    }
}
