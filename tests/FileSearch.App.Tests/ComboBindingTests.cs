using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FileSearch.App.ViewModels;
using FileSearch.Core;

namespace FileSearch.App.Tests;

/// <summary>
/// The mode combo boxes in SearchTabView.xaml bind an enum through SelectedValue. A typo in the binding
/// path would fail silently at run time, so this builds the same binding in code and checks both directions.
/// </summary>
public class ComboBindingTests
{
    private static void OnSta(Action work)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new AggregateException(error);
    }

    private static ComboBox Combo(object items, string path, object dataContext)
    {
        var box = new ComboBox
        {
            ItemsSource = (System.Collections.IEnumerable)items,
            DisplayMemberPath = "Label",
            SelectedValuePath = "Value",
            DataContext = dataContext,
        };
        box.SetBinding(Selector.SelectedValueProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        return box;
    }

    [Fact]
    public void ContentModeComboFollowsTheViewModelBothWays() => OnSta(() =>
    {
        var tab = Vm.NewMain().SelectedTab!;
        var box = Combo(tab.ContentModes, nameof(SearchTabViewModel.ContentMode), tab);

        Assert.Equal(ContentMode.Plain, box.SelectedValue);

        tab.ContentMode = ContentMode.Boolean;
        Assert.Equal(ContentMode.Boolean, box.SelectedValue);

        box.SelectedIndex = tab.ContentModes.ToList().FindIndex(m => m.Value == ContentMode.Regex);
        Assert.Equal(ContentMode.Regex, tab.ContentMode);
    });

    [Fact]
    public void NameModeComboFollowsTheViewModelBothWays() => OnSta(() =>
    {
        var tab = Vm.NewMain().SelectedTab!;
        var box = Combo(tab.NameModes, nameof(SearchTabViewModel.NameMode), tab);

        tab.NameMode = NameMode.Regex;
        Assert.Equal(NameMode.Regex, box.SelectedValue);

        box.SelectedIndex = 0;
        Assert.Equal(NameMode.Dos, tab.NameMode);
    });

    [Fact]
    public void SimpleExpertComboWritesTheSharedUiMode() => OnSta(() =>
    {
        var tab = Vm.NewMain().SelectedTab!;
        var box = Combo(tab.Modes, "Ui." + nameof(UiOptions.Mode), tab);

        Assert.Equal(UiMode.Expert, box.SelectedValue);

        box.SelectedIndex = 0;
        Assert.Equal(UiMode.Simple, tab.Ui.Mode);
        Assert.False(tab.Ui.IsExpert);
    });
}
