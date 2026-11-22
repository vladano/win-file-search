using System.Collections;
using System.Resources;
using System.Windows.Media.Imaging;

namespace FileSearch.App.Tests;

/// <summary>
/// Guards the application icon: MainWindow.xaml loads it from the WPF resources, so a missing or
/// broken file would only show up at run time as a crash or a default icon.
/// </summary>
public class AppIconTests
{
    // WPF stores Resource items in "<assembly>.g.resources" under lower-case relative paths.
    private const string ResourceKey = "assets/filesearch.ico";

    private static byte[] ReadEmbeddedIcon()
    {
        var asm = typeof(App).Assembly;
        using var stream = asm.GetManifestResourceStream(asm.GetName().Name + ".g.resources");
        Assert.NotNull(stream);
        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry entry in reader)
        {
            if ((string)entry.Key != ResourceKey) continue;
            using var data = Assert.IsAssignableFrom<Stream>(entry.Value);
            using var copy = new MemoryStream();
            data.CopyTo(copy);
            return copy.ToArray();
        }
        Assert.Fail($"'{ResourceKey}' is not embedded as a WPF resource.");
        return [];
    }

    [Fact]
    public void IconIsEmbeddedAsAWpfResource() => Assert.NotEmpty(ReadEmbeddedIcon());

    [Fact]
    public void IconHasEverySizeWindowsUses()
    {
        using var ms = new MemoryStream(ReadEmbeddedIcon());
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var sizes = decoder.Frames.Select(f => f.PixelWidth).Order().ToArray();
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 128, 256], sizes);
        Assert.All(decoder.Frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));
    }

    [Fact]
    public void IconFramesHaveTransparentCorners()
    {
        using var ms = new MemoryStream(ReadEmbeddedIcon());
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        foreach (var frame in decoder.Frames)
        {
            var bgra = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            bgra.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Assert.True(pixel[3] == 0, $"top-left pixel of the {frame.PixelWidth} px frame is not transparent");
        }
    }
}
