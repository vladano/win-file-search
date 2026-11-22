namespace FileSearch.App.Services;

/// <summary>
/// Usage: FileSearch.exe [folder] [--name pattern] [--text text] [--start]
///        FileSearch.exe --install-shell | --uninstall-shell
/// </summary>
public sealed record CommandLineOptions(
    string? Folder,
    string? Name,
    string? Text,
    bool Start,
    bool InstallShell,
    bool UninstallShell)
{
    public bool HasSearchRequest => Folder is not null || Name is not null || Text is not null;

    public static CommandLineOptions Parse(IEnumerable<string> args)
    {
        string? folder = null, name = null, text = null;
        bool start = false, install = false, uninstall = false;

        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            switch (list[i].ToLowerInvariant())
            {
                case "--name" when i + 1 < list.Count: name = list[++i]; break;
                case "--text" when i + 1 < list.Count: text = list[++i]; break;
                case "--start": start = true; break;
                case "--install-shell": install = true; break;
                case "--uninstall-shell": uninstall = true; break;
                default:
                    if (!list[i].StartsWith("--", StringComparison.Ordinal))
                        folder = NormalizeFolder(list[i]);
                    break;
            }
        }
        return new CommandLineOptions(folder, name, text, start, install, uninstall);
    }

    /// <summary>
    /// Explorer passes "C:\" quoted, which the CLR parses as C:" (the backslash escapes the quote).
    /// Strip stray quotes and restore the drive root.
    /// </summary>
    private static string NormalizeFolder(string raw)
    {
        var p = raw.Trim().Trim('"');
        if (p.Length == 2 && p[1] == ':') p += "\\";
        if (File.Exists(p)) p = Path.GetDirectoryName(p) ?? p;
        return p;
    }
}
