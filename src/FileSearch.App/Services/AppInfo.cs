using System.Reflection;
using System.Runtime.InteropServices;

namespace FileSearch.App.Services;

/// <summary>
/// What the About window shows: product, version, copyright and the project links.
/// Version and copyright are read from the assembly attributes set in FileSearch.App.csproj
/// (the release workflow sets the version from the git tag).
/// </summary>
public sealed record AppInfo(string Product, string Version, string? Commit, string Copyright)
{
    public const string RepositoryUrl = "https://github.com/vladano/win-file-search";
    public const string ManualUrl = RepositoryUrl + "/blob/main/docs/USER-MANUAL.md";
    public const string IssuesUrl = RepositoryUrl + "/issues/new/choose";
    public const string ReleasesUrl = RepositoryUrl + "/releases/latest";
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    public const string Description = "Fast file content search for Windows.";
    public const string LicenseName = "MIT License";

    /// <summary>"0.1.1" or "0.1.1 (commit bd76b36)".</summary>
    public string VersionText => Commit is null ? Version : $"{Version} (commit {Commit})";

    public static AppInfo FromAssembly(Assembly assembly)
    {
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "File Search";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? assembly.GetName().Version?.ToString(3)
                            ?? "unknown";
        var (version, commit) = SplitVersion(informational);
        return new AppInfo(product, version, commit, copyright);
    }

    /// <summary>
    /// The SDK appends the commit to the informational version ("0.1.1+bd76b36624c2..."). Keep the version,
    /// and a short commit id when there is one.
    /// </summary>
    public static (string Version, string? Commit) SplitVersion(string informational)
    {
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0) return (informational, null);
        var commit = informational[(plus + 1)..];
        return (informational[..plus], commit.Length == 0 ? null : commit[..Math.Min(7, commit.Length)]);
    }

    /// <summary>Text for "Copy details": what a bug report needs to know about this installation.</summary>
    public string Details() =>
        $"{Product} {VersionText}{Environment.NewLine}" +
        $"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}){Environment.NewLine}" +
        $".NET: {RuntimeInformation.FrameworkDescription}{Environment.NewLine}" +
        $"Source: {RepositoryUrl}";
}
