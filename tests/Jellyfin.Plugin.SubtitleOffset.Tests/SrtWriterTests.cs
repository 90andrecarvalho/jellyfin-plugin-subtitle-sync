using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SubtitleOffset.Srt;
using Xunit;

namespace Jellyfin.Plugin.SubtitleOffset.Tests;

public class SrtWriterTests
{
    [Fact]
    public void Write_ProducesValidSrtFormat()
    {
        var entries = new List<SrtEntry>
        {
            new SrtEntry
            {
                SequenceNumber = 1,
                StartTime = TimeSpan.FromMilliseconds(1000),
                EndTime = TimeSpan.FromMilliseconds(4000),
                TextLines = new[] { "Hello, world!" }
            },
            new SrtEntry
            {
                SequenceNumber = 2,
                StartTime = TimeSpan.FromMilliseconds(5000),
                EndTime = TimeSpan.FromMilliseconds(8500),
                TextLines = new[] { "Second line", "with multiline." }
            }
        };

        var result = SrtWriter.Write(entries);

        Assert.Contains("00:00:01,000 --> 00:00:04,000", result);
        Assert.Contains("00:00:05,000 --> 00:00:08,500", result);
        Assert.Contains("Hello, world!", result);
        Assert.Contains("Second line", result);
        Assert.Contains("with multiline.", result);
    }

    [Fact]
    public void Write_FormatsHoursCorrectly()
    {
        var entries = new List<SrtEntry>
        {
            new SrtEntry
            {
                SequenceNumber = 1,
                StartTime = new TimeSpan(0, 1, 30, 45, 123),
                EndTime = new TimeSpan(0, 2, 0, 0, 0),
                TextLines = new[] { "Text" }
            }
        };

        var result = SrtWriter.Write(entries);
        Assert.Contains("01:30:45,123 --> 02:00:00,000", result);
    }

    [Fact]
    public void Write_ZeroTimestamp_FormatsCorrectly()
    {
        var entries = new List<SrtEntry>
        {
            new SrtEntry
            {
                SequenceNumber = 1,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromMilliseconds(500),
                TextLines = new[] { "Start" }
            }
        };

        var result = SrtWriter.Write(entries);
        Assert.Contains("00:00:00,000 --> 00:00:00,500", result);
    }

    [Fact]
    public void Write_RoundTrip_PreservesContent()
    {
        var original = @"1
00:00:01,000 --> 00:00:04,000
Hello, welcome.

2
00:00:05,000 --> 00:00:08,500
Second line.
";

        var entries = SrtParser.Parse(original);
        var output = SrtWriter.Write(entries);
        var reparsed = SrtParser.Parse(output);

        Assert.Equal(entries.Count, reparsed.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(entries[i].StartTime, reparsed[i].StartTime);
            Assert.Equal(entries[i].EndTime, reparsed[i].EndTime);
            Assert.Equal(entries[i].TextLines, reparsed[i].TextLines);
        }
    }

    [Fact]
    public void Write_SingleEntry_NoTrailingBlankLine()
    {
        var entries = new List<SrtEntry>
        {
            new SrtEntry
            {
                SequenceNumber = 1,
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(2),
                TextLines = new[] { "Only one." }
            }
        };

        var result = SrtWriter.Write(entries);
        // Should not end with double newline
        var trimmed = result.TrimEnd('\r', '\n');
        Assert.EndsWith("Only one.", trimmed);
    }
}
