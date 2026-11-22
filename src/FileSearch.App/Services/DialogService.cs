using System.Windows;
using Microsoft.Win32;

namespace FileSearch.App.Services;

/// <summary>
/// Everything the application asks the user through dialogs. View models depend on this interface,
/// so they stay free of windows and tests can answer the dialogs with a fake.
/// </summary>
public interface IDialogService
{
    void ShowInfo(string title, string message);
    void ShowWarning(string title, string message);
    void ShowError(string title, string message);

    /// <summary>Returns the chosen folder, or null when the user cancels.</summary>
    string? PickFolder(string title, string? initialFolder);

    /// <summary>Returns the chosen file to open, or null when the user cancels.</summary>
    string? PickOpenFile(string title, string filter);

    /// <summary>Returns the chosen file to save to, or null when the user cancels.</summary>
    string? PickSaveFile(string title, string filter, string defaultExtension);
}

/// <summary>Real dialogs, owned by the main window.</summary>
public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowInfo(string title, string message) => Show(title, message, MessageBoxImage.Information);

    public void ShowWarning(string title, string message) => Show(title, message, MessageBoxImage.Warning);

    public void ShowError(string title, string message) => Show(title, message, MessageBoxImage.Error);

    public string? PickFolder(string title, string? initialFolder)
    {
        var dlg = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
            dlg.InitialDirectory = initialFolder;
        return Show(dlg) ? dlg.FolderName : null;
    }

    public string? PickOpenFile(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        return Show(dlg) ? dlg.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultExtension)
    {
        var dlg = new SaveFileDialog { Title = title, Filter = filter, DefaultExt = defaultExtension };
        return Show(dlg) ? dlg.FileName : null;
    }

    private static bool Show(CommonDialog dlg) => (Owner is { } o ? dlg.ShowDialog(o) : dlg.ShowDialog()) == true;

    private static void Show(string title, string message, MessageBoxImage image)
    {
        if (Owner is { } o) MessageBox.Show(o, message, title, MessageBoxButton.OK, image);
        else MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }
}

/// <summary>Dialogs that never appear and always answer "cancel". Default for tests and headless use.</summary>
public sealed class NullDialogService : IDialogService
{
    public void ShowInfo(string title, string message) { }
    public void ShowWarning(string title, string message) { }
    public void ShowError(string title, string message) { }
    public string? PickFolder(string title, string? initialFolder) => null;
    public string? PickOpenFile(string title, string filter) => null;
    public string? PickSaveFile(string title, string filter, string defaultExtension) => null;
}
