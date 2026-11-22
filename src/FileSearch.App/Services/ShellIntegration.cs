using Microsoft.Win32;

namespace FileSearch.App.Services;

/// <summary>
/// Adds "Search with File Search" to the Explorer context menu for folders, folder backgrounds and drives.
/// Uses HKCU only, so no administrator rights are needed. On Windows 11 the entry is listed under
/// "Show more options".
/// </summary>
public static class ShellIntegration
{
    private const string MenuText = "Search with File Search";

    private static readonly (string Key, string Arg)[] Targets =
    [
        (@"Software\Classes\Directory\shell\FileSearch", "%1"),
        (@"Software\Classes\Directory\Background\shell\FileSearch", "%V"),
        (@"Software\Classes\Drive\shell\FileSearch", "%1"),
    ];

    public static bool IsInstalled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Targets[0].Key);
        return k is not null;
    }

    public static void Install(string exePath)
    {
        foreach (var (key, arg) in Targets)
        {
            using var k = Registry.CurrentUser.CreateSubKey(key);
            k.SetValue("", MenuText);
            k.SetValue("Icon", $"\"{exePath}\",0");
            using var cmd = k.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" \"{arg}\"");
        }
    }

    public static void Uninstall()
    {
        foreach (var (key, _) in Targets)
            Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
    }
}
