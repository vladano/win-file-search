using System.Collections.Specialized;
using System.ComponentModel;
using FileSearch.App.Services;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

public class MainViewModelTests
{
    [Fact]
    public void StartsWithOneSelectedTab()
    {
        var vm = Vm.NewMain();
        Assert.Single(vm.Tabs);
        Assert.Same(vm.Tabs[0], vm.SelectedTab);
        Assert.Equal("File Search", vm.WindowTitle);
    }

    [Fact]
    public void ExpertModeComesFromSettingsAndIsWrittenBack()
    {
        var settings = new AppSettings { Mode = UiMode.Simple };
        var vm = new MainViewModel(settings);
        Assert.False(vm.Ui.IsExpert);

        vm.Ui.Mode = UiMode.Expert;
        Assert.Equal(UiMode.Expert, settings.Mode);
    }

    [Fact]
    public void AddTabSelectsTheNewTabAndCopiesTheFolder()
    {
        var vm = Vm.NewMain();
        vm.SelectedTab!.LookIn = @"C:\work";

        vm.AddTabCommand.Execute(null);

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(vm.Tabs[1], vm.SelectedTab);
        Assert.Equal(@"C:\work", vm.SelectedTab!.LookIn);
    }

    [Fact]
    public void ClosingTheSelectedTabSelectsANeighbour()
    {
        var vm = Vm.NewMain();
        var first = vm.Tabs[0];
        var second = vm.NewTab();
        var third = vm.NewTab();
        vm.SelectedTab = second;

        vm.CloseTabCommand.Execute(second);

        Assert.Equal([first, third], vm.Tabs);
        Assert.NotNull(vm.SelectedTab);
        Assert.Contains(vm.SelectedTab, vm.Tabs);
    }

    [Fact]
    public void ClosingTheLastTabLeavesAFreshOne()
    {
        var vm = Vm.NewMain();
        var only = vm.Tabs[0];

        vm.CloseTabCommand.Execute(only);

        Assert.Single(vm.Tabs);
        Assert.NotSame(only, vm.Tabs[0]);
        Assert.Same(vm.Tabs[0], vm.SelectedTab);
    }

    [Fact]
    public void ClosingNothingDoesNothing()
    {
        var vm = Vm.NewMain();
        vm.CloseTabCommand.Execute(null);
        Assert.Single(vm.Tabs);
    }

    [Fact]
    public void CommandLineRequestReusesThePristineTab()
    {
        var vm = Vm.NewMain();
        var tab = vm.Tabs[0];

        vm.OpenFromCommandLine(CommandLineOptions.Parse([@"C:\x", "--name", "*.cs", "--text", "foo"]));

        Assert.Single(vm.Tabs);
        Assert.Equal((@"C:\x", "*.cs", "foo"), (tab.LookIn, tab.FileName, tab.ContainingText));
    }

    [Fact]
    public void CommandLineRequestOpensANewTabWhenTheCurrentOneIsInUse()
    {
        var vm = Vm.NewMain();
        vm.Tabs[0].LookIn = @"C:\busy";

        vm.OpenFromCommandLine(CommandLineOptions.Parse([@"C:\other"]));

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Equal(@"C:\busy", vm.Tabs[0].LookIn);
        Assert.Equal(@"C:\other", vm.SelectedTab!.LookIn);
    }

    [Fact]
    public void EmptyCommandLineChangesNothing()
    {
        var vm = Vm.NewMain();
        vm.OpenFromCommandLine(CommandLineOptions.Parse([]));
        Assert.Single(vm.Tabs);
        Assert.True(vm.Tabs[0].IsPristine);
    }

    [Fact]
    public async Task CommandLineStartRunsTheSearch()
    {
        using var dir = new TempDir();
        dir.Write("a.prg", "ALIAS here");
        var vm = Vm.NewMain();

        vm.OpenFromCommandLine(CommandLineOptions.Parse([dir.Root, "--name", "*.prg", "--text", "ALIAS", "--start"]));
        await WaitUntil(() => vm.SelectedTab!.HasSearched && !vm.SelectedTab.IsRunning);

        Assert.Single(vm.SelectedTab!.Results);
    }

    [Fact]
    public async Task WindowTitleDescribesTheSearch()
    {
        using var dir = new TempDir();
        dir.Write("a.prg", "ALIAS");
        var vm = Vm.NewMain();
        var tab = vm.SelectedTab!;
        tab.FileName = "*.prg";
        tab.ContainingText = "ALIAS";
        tab.LookIn = dir.Root;

        var titleChanges = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.WindowTitle)) titleChanges++; };
        await tab.StartCommand.ExecuteAsync(null);

        Assert.Equal($"File Search - Search: ALIAS in {dir.Root} (*.prg)", vm.WindowTitle);
        Assert.True(titleChanges > 0);
    }

    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(25);
        }
    }
}
