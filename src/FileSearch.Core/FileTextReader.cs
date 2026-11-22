using System.IO.Compression;
using System.Text;
using System.Xml;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FileSearch.Core;

internal enum ReadStatus { Ok, Binary, TooLarge, Error }

/// <summary>Loads searchable text from plain files, Office Open XML files and PDFs.</summary>
internal static class FileTextReader
{
    private static readonly HashSet<string> OpenXmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm",
    };

    public static ReadStatus TryRead(string path, long length, SearchCriteria criteria, CancellationToken ct, out string text) =>
        TryRead(path, length, criteria, NullSearchLogger.Instance, ct, out text);

    public static ReadStatus TryRead(
        string path, long length, SearchCriteria criteria, ISearchLogger log, CancellationToken ct, out string text)
    {
        text = "";
        try
        {
            if (criteria.SearchDocuments)
            {
                var ext = Path.GetExtension(path);
                var isOpenXml = OpenXmlExtensions.Contains(ext);
                var isPdf = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
                if (isOpenXml || isPdf)
                {
                    // Documents are compressed, so the same limit also caps the extracted text (see below).
                    if (length > criteria.MaxContentFileSizeBytes) return ReadStatus.TooLarge;
                    var extracted = isOpenXml
                        ? ExtractOpenXml(path, criteria.MaxContentFileSizeBytes)
                        : ExtractPdf(path, criteria.MaxContentFileSizeBytes, ct);
                    text = Normalize(extracted);
                    return ReadStatus.Ok;
                }
            }

            if (length == 0) return ReadStatus.Ok;
            if (length > criteria.MaxContentFileSizeBytes) return ReadStatus.TooLarge;

            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                       bufferSize: 1, FileOptions.SequentialScan))
            {
                bytes = new byte[fs.Length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var n = fs.Read(bytes, read, bytes.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read < bytes.Length) Array.Resize(ref bytes, read);
            }

            if (!TryDecode(bytes, out var decoded)) return ReadStatus.Binary;
            text = Normalize(decoded);
            return ReadStatus.Ok;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExtractionLimitException ex)
        {
            log.Warning($"Document text exceeds the size limit, file skipped: {path}", ex);
            return ReadStatus.TooLarge;
        }
        catch (Exception ex)
        {
            log.Warning($"Could not read file: {path}", ex);
            return ReadStatus.Error;
        }
    }

    /// <summary>Thrown when a document would expand to more text than the configured limit (zip bombs, huge PDFs).</summary>
    private sealed class ExtractionLimitException() : Exception("Extracted document text is larger than the size limit.");

    /// <summary>Counts bytes read from a stream and fails once a shared budget is used up.</summary>
    private sealed class BudgetStream(Stream inner, long[] remaining) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            remaining[0] -= n;
            if (remaining[0] < 0) throw new ExtractionLimitException();
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            remaining[0] -= n;
            if (remaining[0] < 0) throw new ExtractionLimitException();
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
    private static string Normalize(string s) => s.Contains('\r') ? s.Replace("\r\n", "\n").Replace('\r', '\n') : s;

    private static bool TryDecode(byte[] b, out string text)
    {
        text = "";
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(b, 3, b.Length - 3);
            return true;
        }
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0)
        {
            text = Encoding.UTF32.GetString(b, 4, b.Length - 4);
            return true;
        }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(b, 2, b.Length - 2);
            return true;
        }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            return true;
        }

        // No BOM: a NUL byte in the first 8 KB means binary.
        var probe = Math.Min(b.Length, 8192);
        if (b.AsSpan(0, probe).IndexOf((byte)0) >= 0) return false;

        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(b);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(b);
        }
        return true;
    }

    private static string ExtractOpenXml(string path, long maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var sb = new StringBuilder();
        var budget = new[] { maxBytes }; // decompressed bytes still allowed, shared by all entries
        foreach (var e in zip.Entries)
        {
            var n = e.FullName;
            var wanted = n is "word/document.xml" or "xl/sharedStrings.xml"
                         || (n.StartsWith("word/header", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal))
                         || (n.StartsWith("word/footer", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal))
                         || (n.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal));
            if (!wanted) continue;
            using var s = new BudgetStream(e.Open(), budget);
            AppendXmlText(s, sb);
        }
        return sb.ToString();
    }

    private static void AppendXmlText(Stream s, StringBuilder sb)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var r = XmlReader.Create(s, settings);
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                var name = r.LocalName;
                if (name == "t" && !r.IsEmptyElement)
                {
                    sb.Append(r.ReadElementContentAsString());
                    continue;
                }
                if (name is "br" or "tab" or "cr") sb.Append(name == "tab" ? ' ' : '\n');
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName is "p" or "si")
            {
                sb.Append('\n');
            }
            r.Read();
        }
    }

    private static string ExtractPdf(string path, long maxChars, CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = PdfDocument.Open(fs, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            sb.AppendLine(ContentOrderTextExtractor.GetText(page));
            if (sb.Length > maxChars) throw new ExtractionLimitException();
        }
        return sb.ToString();
    }
}
