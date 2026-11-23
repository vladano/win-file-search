using System.Windows;
using FileSearch.App.Services;
using FileSearch.App.ViewModels;

namespace FileSearch.App;

/// <summary>Window chrome only: placement, closing, and forwarding toolbar clicks to <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplySavedBounds();
        Closing += OnClosing;
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ApplySavedBounds()
    {
        var s = Vm.Settings;
        if (s.Width is { } w && s.Height is { } h && s.Left is { } l && s.Top is { } t
            && l + 100 > SystemParameters.VirtualScreenLeft && t >= SystemParameters.VirtualScreenTop - 10
            && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (l, t, Math.Max(w, MinWidth), Math.Max(h, MinHeight));
        }
        if (s.Maximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var s = Vm.Settings;
        s.Maximized = WindowState == WindowState.Maximized;
        var b = CurrentBounds();
        (s.Left, s.Top, s.Width, s.Height) = (b.Left, b.Top, b.Width, b.Height);
        Vm.Shutdown();
    }

    private Rect CurrentBounds() => WindowState == WindowState.Normal
        ? new Rect(Left, Top, Width, Height)
        : RestoreBounds;

    // ---- toolbar and menu: thin forwards to the view model ----

    private void OpenDefinition_Click(object sender, RoutedEventArgs e) => Vm.OpenDefinition();

    private void SaveDefinition_Click(object sender, RoutedEventArgs e) => Vm.SaveDefinition();

    private void Export_Click(object sender, RoutedEventArgs e) => Vm.ExportResults(Vm.SelectedTab);

    private void Help_Click(object sender, RoutedEventArgs e) => Vm.ShowHelp();

    private void About_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow(AppInfo.FromAssembly(typeof(App).Assembly)) { Owner = this }.ShowDialog();

    private void InstallShell_Click(object sender, RoutedEventArgs e)
    {
        const string title = "Explorer context menu";
        try
        {
            ShellIntegration.Install(Environment.ProcessPath!);
            Vm.Dialogs.ShowInfo(title,
                "Added \"Search with File Search\" to the Explorer context menu for folders and drives.\n\n" +
                "On Windows 11 it appears under \"Show more options\".");
        }
        catch (Exception ex)
        {
            Vm.Fail(title, "Could not add the Explorer context menu", ex);
        }
    }

    private void UninstallShell_Click(object sender, RoutedEventArgs e)
    {
        const string title = "Explorer context menu";
        try
        {
            ShellIntegration.Uninstall();
            Vm.Dialogs.ShowInfo(title, "Removed from the Explorer context menu.");
        }
        catch (Exception ex)
        {
            Vm.Fail(title, "Could not remove the Explorer context menu", ex);
        }
    }
}
