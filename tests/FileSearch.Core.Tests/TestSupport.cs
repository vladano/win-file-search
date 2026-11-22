using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using FileSearch.Core;

namespace FileSearch.Core.Tests;

/// <summary>A temporary directory that is deleted when disposed.</summary>
internal sealed class TempDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fs-test-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Root);

    public string PathOf(string relative) => Path.Combine(Root, relative);

    public string Write(string relative, string content, Encoding? encoding = null)
    {
        var p = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, encoding ?? new UTF8Encoding(false));
        return p;
    }

    public string WriteBytes(string relative, byte[] bytes)
    {
        var p = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}

internal static class Engine
{
    public static async Task<(List<FileResult> Results, SearchSummary Summary)> Run(
        SearchCriteria criteria, CancellationToken ct = default, PauseControl? pause = null)
    {
        var bag = new ConcurrentBag<FileResult>();
        var summary = await new SearchEngine().RunAsync(criteria, bag.Add, pause: pause, ct: ct);
        return (bag.OrderBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase).ToList(), summary);
    }

    public static string[] Names(IEnumerable<FileResult> results) =>
        results.Select(r => r.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
}

/// <summary>Builds small Office Open XML and PDF files for tests.</summary>
internal static class SampleFiles
{
    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Zip(string path, params (string Entry, string Xml)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, xml) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
    }

    public static void Docx(string path, params string[] paragraphs) =>
        Zip(path, ("word/document.xml",
            $"<w:document xmlns:w=\"{WordNs}\"><w:body>"
            + string.Concat(paragraphs.Select(p => $"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>"))
            + "</w:body></w:document>"));

    public static void Xlsx(string path, params string[] strings) =>
        Zip(path, ("xl/sharedStrings.xml",
            $"<sst xmlns=\"{SheetNs}\">" + string.Concat(strings.Select(s => $"<si><t>{s}</t></si>")) + "</sst>"));

    public static void Pptx(string path, params string[] slideTexts)
    {
        var entries = slideTexts.Select((t, i) => ($"ppt/slides/slide{i + 1}.xml",
            $"<p:sld xmlns:p=\"urn:p\" xmlns:a=\"{DrawingNs}\"><p:cSld><p:spTree><a:p><a:r><a:t>{t}</a:t></a:r></a:p></p:spTree></p:cSld></p:sld>"));
        Zip(path, entries.ToArray());
    }

    /// <summary>Writes a one-page PDF containing <paramref name="text"/> (valid xref table).</summary>
    public static void Pdf(string path, string text)
    {
        var stream = $"BT /F1 12 Tf 20 100 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(sb.ToString()));
    }
}
