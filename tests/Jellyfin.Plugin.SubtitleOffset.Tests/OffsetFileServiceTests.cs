using System;
using System.IO;
using Jellyfin.Plugin.SubtitleOffset.Services;
using Xunit;

namespace Jellyfin.Plugin.SubtitleOffset.Tests;

public class OffsetFileServiceTests
{
    [Theory]
    [InlineData("Movie (2020).en.Offset+2000ms.srt", true)]
    [InlineData("Movie (2020).en.Offset-1500ms.srt", true)]
    [InlineData("Movie (2020).en.Offset+0ms.srt", true)]
    [InlineData("Movie (2020).en.srt", false)]
    [InlineData("Movie (2020).en.SDH.srt", false)]
    [InlineData("Movie.Offset.2020.en.srt", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsOffsetFile_DetectsCorrectly(string? path, bool expected)
    {
        Assert.Equal(expected, OffsetFileService.IsOffsetFile(path));
    }

    [Fact]
    public void FindOriginalSubtitlePath_PrefersNonOffsetOriginal()
    {
        var testDir = CreateTestDirectory();

        try
        {
            File.WriteAllText(Path.Combine(testDir, "Movie.en.srt"), string.Empty);
            File.WriteAllText(Path.Combine(testDir, "Movie.en.SDH.srt"), string.Empty);
            File.WriteAllText(Path.Combine(testDir, "Movie.en.Offset+1500ms.srt"), string.Empty);

            var originalPath = OffsetFileService.FindOriginalSubtitlePath(testDir, "Movie", "en");

            Assert.Equal(Path.Combine(testDir, "Movie.en.srt"), originalPath);
        }
        finally
        {
            DeleteTestDirectory(testDir);
        }
    }

    [Fact]
    public void FindOriginalSubtitlePath_SupportsUndLanguageFallback()
    {
        var testDir = CreateTestDirectory();

        try
        {
            File.WriteAllText(Path.Combine(testDir, "Movie.srt"), string.Empty);
            File.WriteAllText(Path.Combine(testDir, "Movie.und.Offset+1500ms.srt"), string.Empty);

            var originalPath = OffsetFileService.FindOriginalSubtitlePath(testDir, "Movie", "und");

            Assert.Equal(Path.Combine(testDir, "Movie.srt"), originalPath);
        }
        finally
        {
            DeleteTestDirectory(testDir);
        }
    }

    [Fact]
    public void GetExistingOffsetMs_ReturnsOffsetFromFilename()
    {
        var testDir = CreateTestDirectory();

        try
        {
            File.WriteAllText(Path.Combine(testDir, "Movie.en.Offset-1750ms.srt"), string.Empty);

            var offsetMs = OffsetFileService.GetExistingOffsetMs(testDir, "Movie", "en");

            Assert.Equal(-1750, offsetMs);
        }
        finally
        {
            DeleteTestDirectory(testDir);
        }
    }

    private static string CreateTestDirectory()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "OffsetFileServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        return testDir;
    }

    private static void DeleteTestDirectory(string testDir)
    {
        if (Directory.Exists(testDir))
        {
            Directory.Delete(testDir, recursive: true);
        }
    }
}
