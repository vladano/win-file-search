using System.Globalization;
using System.Text;

namespace FileSearch.Core;

public static class ReportBuilder
{
    /// <summary>Human-readable report. Numbers follow the user's culture, like the rest of the UI.</summary>
    public static string ToText(IEnumerable<FileResult> results)
    {
        var culture = CultureInfo.CurrentCulture;
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            sb.AppendLine(culture, $"{r.FullPath}  ({r.Length / 1024.0:N2} kB, {r.Modified:yyyy-MM-dd HH:mm})");
            foreach (var h in r.Hits)
                sb.AppendLine(culture, $"    {h.LineNumber,6}: {h.Text.TrimStart()}");
        }
        return sb.ToString();
    }

    /// <summary>Machine-readable CSV. Always culture-invariant so the file is the same on every machine.</summary>
    public static string ToCsv(IEnumerable<FileResult> results)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Path,Name,Location,SizeKB,Modified,Line,Text");
        foreach (var r in results)
        {
            var prefix = string.Join(',', Csv(r.FullPath), Csv(r.Name), Csv(r.Directory),
                (r.Length / 1024.0).ToString("0.##", inv),
                Csv(r.Modified.ToString("yyyy-MM-dd HH:mm:ss", inv)));
            if (r.Hits.Count == 0)
            {
                sb.AppendLine(prefix + ",,");
                continue;
            }
            foreach (var h in r.Hits)
                sb.AppendLine(inv, $"{prefix},{h.LineNumber},{Csv(h.Text)}");
        }
        return sb.ToString();
    }

    private static string Csv(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
