using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using FileSearch.App.Services;

namespace FileSearch.App;

/// <summary>Shows the version, license and project links (source code, manual, releases, issues).</summary>
public partial class AboutWindow : Window
{
    private readonly AppInfo _info;
    private readonly Action<string> _openUrl;

    /// <param name="openUrl">Opens a link; defaults to the user's browser. Tests pass a fake.</param>
    public AboutWindow(AppInfo info, Action<string>? openUrl = null)
    {
        InitializeComponent();
        _info = info;
        _openUrl = openUrl ?? OpenInBrowser;

        ProductText.Text = info.Product;
        VersionText.Text = "Version " + info.VersionText;
        DescriptionText.Text = AppInfo.Description;
        CopyrightText.Text = info.Copyright;
        RepositoryUrlText.Text = AppInfo.RepositoryUrl;
        LicenseRun.Text = "Free and open source: " + AppInfo.LicenseName;

        RepositoryLink.NavigateUri = new Uri(AppInfo.RepositoryUrl);
        ManualLink.NavigateUri = new Uri(AppInfo.ManualUrl);
        ReleasesLink.NavigateUri = new Uri(AppInfo.ReleasesUrl);
        IssuesLink.NavigateUri = new Uri(AppInfo.IssuesUrl);
        LicenseLink.NavigateUri = new Uri(AppInfo.LicenseUrl);
    }

    /// <summary>Text that "Copy details" puts on the clipboard.</summary>
    public string DetailsText => _info.Details();

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            _openUrl(e.Uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the link:\n{e.Uri.AbsoluteUri}\n\n{ex.Message}",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DetailsText);
            CopyButton.Content = "Copied";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard busy (another program holds it); the user can simply try again.
        }
    }

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
