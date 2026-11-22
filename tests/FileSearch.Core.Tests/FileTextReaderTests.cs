using System.Text;
using FileSearch.Core;

namespace FileSearch.Core.Tests;

public sealed class FileTextReaderTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private (ReadStatus Status, string Text) Read(string path, SearchCriteria? c = null)
    {
        var status = FileTextReader.TryRead(path, new FileInfo(path).Length, c ?? new SearchCriteria(), CancellationToken.None, out var text);
        return (status, text);
    }

    [Fact]
    public void Utf8WithBom()
    {
        var p = _dir.Write("a.txt", "héllo", new UTF8Encoding(true));
        Assert.Equal((ReadStatus.Ok, "héllo"), Read(p));
    }

    [Fact]
    public void Utf8WithoutBom()
    {
        var p = _dir.Write("a.txt", "naïve ünïcode");
        Assert.Equal((ReadStatus.Ok, "naïve ünïcode"), Read(p));
    }

    [Fact]
    public void InvalidUtf8FallsBackToLatin1()
    {
        var p = _dir.WriteBytes("a.txt", [0x63, 0x61, 0x66, 0xE9]); // "café" in Latin-1
        Assert.Equal((ReadStatus.Ok, "café"), Read(p));
    }

    [Theory]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32")]
    public void BomEncodings(string kind)
    {
        Encoding enc = kind switch
        {
            "utf16le" => Encoding.Unicode,
            "utf16be" => Encoding.BigEndianUnicode,
            _ => Encoding.UTF32,
        };
        var p = _dir.Write("a.txt", "wide text", enc);
        Assert.Equal((ReadStatus.Ok, "wide text"), Read(p));
    }

    [Fact]
    public void LineEndingsAreNormalizedToLf()
    {
        var p = _dir.WriteBytes("a.txt", Encoding.ASCII.GetBytes("a\r\nb\rc\nd"));
        Assert.Equal("a\nb\nc\nd", Read(p).Text);
    }

    [Fact]
    public void NulBytesMeanBinary()
    {
        var p = _dir.WriteBytes("a.bin", [0x41, 0x00, 0x42]);
        Assert.Equal(ReadStatus.Binary, Read(p).Status);
    }

    [Fact]
    public void EmptyFileIsOkAndEmpty()
    {
        var p = _dir.WriteBytes("empty.txt", []);
        Assert.Equal((ReadStatus.Ok, ""), Read(p));
    }

    [Fact]
    public void FilesOverTheLimitAreTooLarge()
    {
        var p = _dir.Write("big.txt", new string('x', 100));
        Assert.Equal(ReadStatus.TooLarge, Read(p, new SearchCriteria { MaxContentFileSizeBytes = 10 }).Status);
    }

    [Fact]
    public void MissingFileIsAnError()
    {
        var status = FileTextReader.TryRead(_dir.PathOf("nope.txt"), 5, new SearchCriteria(), CancellationToken.None, out _);
        Assert.Equal(ReadStatus.Error, status);
    }

    [Fact]
    public void ReadsFilesOpenedByAnotherProcess()
    {
        var p = _dir.Write("locked.txt", "still readable");
        using var holder = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal((ReadStatus.Ok, "still readable"), Read(p));
    }

    [Fact]
    public void Docx()
    {
        var p = _dir.PathOf("a.docx");
        SampleFiles.Docx(p, "First paragraph", "Second ALIAS paragraph");
        var (status, text) = Read(p);
        Assert.Equal(ReadStatus.Ok, status);
        Assert.Equal("First paragraph\nSecond ALIAS paragraph\n", text);
    }

    [Fact]
    public void Xlsx()
    {
        var p = _dir.PathOf("a.xlsx");
        SampleFiles.Xlsx(p, "Revenue", "ALIAS total");
        var (status, text) = Read(p);
        Assert.Equal(ReadStatus.Ok, status);
        Assert.Contains("ALIAS total", text);
        Assert.Contains("Revenue", text);
    }

    [Fact]
    public void Pptx()
    {
        var p = _dir.PathOf("a.pptx");
        SampleFiles.Pptx(p, "Slide one", "Slide ALIAS two");
        var (status, text) = Read(p);
        Assert.Equal(ReadStatus.Ok, status);
        Assert.Contains("Slide ALIAS two", text);
    }

    [Fact]
    public void Pdf()
    {
        var p = _dir.PathOf("a.pdf");
        SampleFiles.Pdf(p, "Hello PDFTOKEN world");
        var (status, text) = Read(p);
        Assert.Equal(ReadStatus.Ok, status);
        Assert.Contains("PDFTOKEN", text);
    }

    [Fact]
    public void DocumentExtensionsAreCaseInsensitive()
    {
        var p = _dir.PathOf("UPPER.DOCX");
        SampleFiles.Docx(p, "shouting ALIAS");
        Assert.Contains("ALIAS", Read(p).Text);
    }

    [Fact]
    public void DocumentsAreTreatedAsBinaryWhenDocumentSearchIsOff()
    {
        var p = _dir.PathOf("a.docx");
        SampleFiles.Docx(p, "hidden ALIAS");
        Assert.Equal(ReadStatus.Binary, Read(p, new SearchCriteria { SearchDocuments = false }).Status);
    }

    [Fact]
    public void CorruptDocumentsAreAnErrorNotACrash()
    {
        var docx = _dir.Write("bad.docx", "this is not a zip file");
        var pdf = _dir.Write("bad.pdf", "this is not a pdf");
        Assert.Equal(ReadStatus.Error, Read(docx).Status);
        Assert.Equal(ReadStatus.Error, Read(pdf).Status);
    }

    [Fact]
    public void CancellationIsNotSwallowed()
    {
        var p = _dir.PathOf("a.pdf");
        SampleFiles.Pdf(p, "text");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            FileTextReader.TryRead(p, new FileInfo(p).Length, new SearchCriteria(), cts.Token, out _));
    }
}
