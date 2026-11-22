using System.Collections.Specialized;
using System.ComponentModel;
using FileSearch.App.Services;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

public class DateFilterTests
{
    [Fact]
    public void CheckingTheBoxFillsInToday()
    {
        var f = new DateFilter { Use = true };
        Assert.Equal(DateTime.Today, f.Value);
    }

    [Fact]
    public void UncheckingClearsTheDate()
    {
        var f = new DateFilter { Value = new DateTime(2024, 3, 4) };
        Assert.True(f.Use);
        f.Use = false;
        Assert.Null(f.Value);
    }

    [Fact]
    public void PickingADateChecksTheBox()
    {
        var f = new DateFilter();
        f.Value = new DateTime(2024, 3, 4);
        Assert.True(f.Use);
    }

    [Fact]
    public void ClearingTheDateUnchecksTheBox()
    {
        var f = new DateFilter { Value = new DateTime(2024, 3, 4) };
        f.Value = null;
        Assert.False(f.Use);
    }

    [Fact]
    public void ClearMethod()
    {
        var f = new DateFilter { Value = new DateTime(2024, 3, 4) };
        f.Clear();
        Assert.Null(f.Value);
        Assert.False(f.Use);
    }

    [Fact]
    public void BoundsCoverWholeDays()
    {
        var f = new DateFilter { Value = new DateTime(2024, 3, 4, 15, 30, 0) };
        Assert.Equal(new DateTime(2024, 3, 4, 0, 0, 0), f.AfterBound);
        Assert.Equal(new DateTime(2024, 3, 4, 23, 59, 59).AddTicks(9_999_999), f.BeforeBound);
    }

    [Fact]
    public void BoundsAreNullWithoutADate()
    {
        var f = new DateFilter();
        Assert.Null(f.AfterBound);
        Assert.Null(f.BeforeBound);
    }
}
