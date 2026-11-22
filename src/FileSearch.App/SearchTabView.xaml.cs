using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FileSearch.App.ViewModels;

namespace FileSearch.App;

public partial class SearchTabView : UserControl
{
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private string? _sortProperty;

    public SearchTabView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Vm is { } vm) vm.Ui.PropertyChanged += OnUiChanged;
            FileNameBox.Focus();
        };
        Unloaded += (_, _) =>
        {
            if (Vm is { } vm) vm.Ui.PropertyChanged -= OnUiChanged;
        };
    }

    private SearchTabViewModel? Vm => DataContext as SearchTabViewModel;

    // In Simple mode the Options and Dates tabs are hidden, so fall back to Main.
    private void OnUiChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiOptions.IsExpert) && Vm?.Ui.IsExpert == false)
            CriteriaTabs.SelectedIndex = 0;
    }

    // ---- sorting ----

    private static readonly Dictionary<string, string> SortKeys = new()
    {
        ["Name"] = nameof(ResultItem.Name),
        ["Location"] = nameof(ResultItem.Directory),
        ["Size"] = nameof(ResultItem.SizeKb),
        ["Modified"] = nameof(ResultItem.Modified),
        ["Hits"] = nameof(ResultItem.HitCount),
    };

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column.Header: string header }) return;
        if (!SortKeys.TryGetValue(header, out var prop)) return;

        _sortDirection = _sortProperty == prop && _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortProperty = prop;

        var view = CollectionViewSource.GetDefaultView(ResultsList.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(prop, _sortDirection));
    }

    // ---- result actions ----

    private ResultItem? Current => ResultsList.SelectedItem as ResultItem;

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks on the header or empty space.
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(ResultsList, d) is ListViewItem)
            OpenCurrent();
    }

    private void Hits_DoubleClick(object sender, MouseButtonEventArgs e) => OpenCurrent();

    private void OpenFile_Click(object sender, RoutedEventArgs e) => OpenCurrent();

    private void OpenCurrent()
    {
        if (Current is not { } r) return;
        Launch(new ProcessStartInfo(r.FullPath) { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } r) return;
        Launch(new ProcessStartInfo("explorer.exe", $"/select,\"{r.FullPath}\"") { UseShellExecute = true });
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(string.Join(Environment.NewLine, ResultsList.SelectedItems.OfType<ResultItem>().Select(r => r.FullPath)));

    private void CopyName_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(string.Join(Environment.NewLine, ResultsList.SelectedItems.OfType<ResultItem>().Select(r => r.Name)));

    private void Launch(ProcessStartInfo psi)
    {
        try { Process.Start(psi); }
        catch (Exception ex)
        {
            Vm?.ShowLaunchError(ex);
        }
    }

    private static void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy */ }
    }

    // ---- reports ----

    private void CopyReport_Click(object sender, RoutedEventArgs e) => CopyToClipboard(Vm?.ReportText ?? "");
}
