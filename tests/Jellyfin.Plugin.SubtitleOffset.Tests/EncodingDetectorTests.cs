using System.Linq;
using System.Text;
using Jellyfin.Plugin.SubtitleOffset.Srt;
using Xunit;

namespace Jellyfin.Plugin.SubtitleOffset.Tests;

public class EncodingDetectorTests
{
    [Fact]
    public void Detect_Utf8WithBom_ReturnsUtf8WithBom()
    {
        var content = "Hello";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(content)).ToArray();

        var encoding = EncodingDetector.Detect(bytes);

        Assert.Equal(Encoding.UTF8.Preamble.Length > 0 ? new UTF8Encoding(true).GetPreamble().Length : 0,
            encoding.GetPreamble().Length > 0 ? encoding.GetPreamble().Length : 0);
    }

    [Fact]
    public void Detect_Utf8WithoutBom_ReturnsUtf8NoBom()
    {
        var bytes = Encoding.UTF8.GetBytes("Hello, world!");
        var encoding = EncodingDetector.Detect(bytes);

        Assert.Equal(0, encoding.GetPreamble().Length);
    }

    [Fact]
    public void Detect_Latin1Characters_ReturnsLatin1()
    {
        // Create bytes that are valid Latin-1 but invalid UTF-8
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0xE9, 0xE8, 0xF1 }; // "Hello éèñ" in Latin-1
        var encoding = EncodingDetector.Detect(bytes);

        Assert.Equal(Encoding.Latin1, encoding);
    }

    [Fact]
    public void Detect_Utf16Le_ReturnsUnicode()
    {
        var content = "Hello";
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(content)).ToArray();

        var encoding = EncodingDetector.Detect(bytes);
        Assert.Equal(Encoding.Unicode, encoding);
    }
}
