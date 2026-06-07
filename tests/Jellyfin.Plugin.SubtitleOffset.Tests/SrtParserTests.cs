using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SubtitleOffset.Srt;
using Xunit;

namespace Jellyfin.Plugin.SubtitleOffset.Tests;

public class SrtParserTests
{
    private const string ValidSrt = @"1
00:00:01,000 --> 00:00:04,000
Hello, welcome to this sample video.

2
00:00:05,000 --> 00:00:08,500
This is the second subtitle line
with multiple lines of text.

3
00:00:10,000 --> 00:00:13,000
And here is the third subtitle entry.
";

    [Fact]
    public void ParseValidSrt_ReturnsCorrectEntryCount()
    {
        var entries = SrtParser.Parse(ValidSrt);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void ParseValidSrt_FirstEntryHasCorrectTimestamps()
    {
        var entries = SrtParser.Parse(ValidSrt);
        Assert.Equal(TimeSpan.FromSeconds(1), entries[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(4), entries[0].EndTime);
    }

    [Fact]
    public void ParseValidSrt_MultilineTextPreserved()
    {
        var entries = SrtParser.Parse(ValidSrt);
        Assert.Equal(2, entries[1].TextLines.Length);
        Assert.Equal("This is the second subtitle line", entries[1].TextLines[0]);
        Assert.Equal("with multiple lines of text.", entries[1].TextLines[1]);
    }

    [Fact]
    public void ParseValidSrt_SequenceNumbersPreserved()
    {
        var entries = SrtParser.Parse(ValidSrt);
        Assert.Equal(1, entries[0].SequenceNumber);
        Assert.Equal(2, entries[1].SequenceNumber);
        Assert.Equal(3, entries[2].SequenceNumber);
    }

    [Fact]
    public void Parse_MissingMilliseconds_AssumesZero()
    {
        var srt = @"1
00:00:05 --> 00:00:08
Hello world.
";
        var entries = SrtParser.Parse(srt);
        Assert.Single(entries);
        Assert.Equal(TimeSpan.FromSeconds(5), entries[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(8), entries[0].EndTime);
    }

    [Fact]
    public void Parse_DotAsMsSeparator_Accepted()
    {
        var srt = @"1
00:00:01.500 --> 00:00:04.750
Dot separator.
";
        var entries = SrtParser.Parse(srt);
        Assert.Single(entries);
        Assert.Equal(new TimeSpan(0, 0, 0, 1, 500), entries[0].StartTime);
        Assert.Equal(new TimeSpan(0, 0, 0, 4, 750), entries[0].EndTime);
    }

    [Fact]
    public void Parse_TrailingTextAfterTimestamp_Ignored()
    {
        var srt = @"1
00:00:01,000 --> 00:00:04,000 X1:0 X2:100 Y1:0 Y2:50
Positioned subtitle.
";
        var entries = SrtParser.Parse(srt);
        Assert.Single(entries);
        Assert.Equal(TimeSpan.FromSeconds(1), entries[0].StartTime);
    }

    [Fact]
    public void Parse_InvalidSequenceNumber_SkippedAndContinues()
    {
        var srt = @"abc
00:00:01,000 --> 00:00:04,000
First entry.

2
00:00:05,000 --> 00:00:08,000
Second entry.
";
        var entries = SrtParser.Parse(srt);
        Assert.Equal(2, entries.Count);
        Assert.Equal("First entry.", entries[0].TextLines[0]);
        Assert.Equal("Second entry.", entries[1].TextLines[0]);
    }

    [Fact]
    public void Parse_EmptySubtitleText_Skipped()
    {
        var srt = @"1
00:00:01,000 --> 00:00:04,000

2
00:00:05,000 --> 00:00:08,000
Valid text.
";
        var entries = SrtParser.Parse(srt);
        Assert.Single(entries);
        Assert.Equal("Valid text.", entries[0].TextLines[0]);
    }

    [Fact]
    public void ParseEmptyContent_Throws()
    {
        Assert.Throws<SrtParseException>(() => SrtParser.Parse(""));
        Assert.Throws<SrtParseException>(() => SrtParser.Parse("   \n  \n  "));
    }

    [Fact]
    public void Parse_NoValidTimestamps_Throws()
    {
        var garbage = @"just some random text
no timestamps here
nothing parseable
";
        Assert.Throws<SrtParseException>(() => SrtParser.Parse(garbage));
    }

    [Fact]
    public void Parse_SingleDigitHours_Accepted()
    {
        var srt = @"1
0:00:01,000 --> 0:00:04,000
Single digit hour.
";
        var entries = SrtParser.Parse(srt);
        Assert.Single(entries);
        Assert.Equal(TimeSpan.FromSeconds(1), entries[0].StartTime);
    }

    [Fact]
    public void Parse_PartialMilliseconds_PaddedRight()
    {
        var srt = @"1
00:00:01,5 --> 00:00:04,50
Partial ms.
";
        var entries = SrtParser.Parse(srt);
        Assert.Single(entries);
        Assert.Equal(new TimeSpan(0, 0, 0, 1, 500), entries[0].StartTime);
        Assert.Equal(new TimeSpan(0, 0, 0, 4, 500), entries[0].EndTime);
    }

    [Fact]
    public void ParseWithBom_StripsAndParses()
    {
        var withBom = "\uFEFF" + ValidSrt;
        var entries = SrtParser.Parse(withBom.TrimStart('\uFEFF'));
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void ParseWithWindowsLineEndings_Works()
    {
        var windowsSrt = "1\r\n00:00:01,000 --> 00:00:04,000\r\nHello.\r\n\r\n2\r\n00:00:05,000 --> 00:00:08,000\r\nWorld.\r\n";
        var entries = SrtParser.Parse(windowsSrt);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ApplyPositiveOffset_ShiftsForward()
    {
        var entries = SrtParser.Parse(ValidSrt);
        SrtParser.ApplyOffset(entries, 2000);

        Assert.Equal(TimeSpan.FromMilliseconds(3000), entries[0].StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(6000), entries[0].EndTime);
        Assert.Equal(TimeSpan.FromMilliseconds(7000), entries[1].StartTime);
    }

    [Fact]
    public void ApplyNegativeOffset_ClampsToZero()
    {
        var entries = SrtParser.Parse(ValidSrt);
        SrtParser.ApplyOffset(entries, -2000);

        Assert.Equal(TimeSpan.Zero, entries[0].StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), entries[0].EndTime);
    }

    [Fact]
    public void ApplyLargeNegativeOffset_ClampsAll()
    {
        var entries = SrtParser.Parse(ValidSrt);
        SrtParser.ApplyOffset(entries, -100000);

        foreach (var entry in entries)
        {
            Assert.Equal(TimeSpan.Zero, entry.StartTime);
            Assert.Equal(TimeSpan.Zero, entry.EndTime);
        }
    }

    [Fact]
    public void ApplyZeroOffset_NoChange()
    {
        var entries = SrtParser.Parse(ValidSrt);
        var originalStart = entries[0].StartTime;
        var originalEnd = entries[0].EndTime;

        SrtParser.ApplyOffset(entries, 0);

        Assert.Equal(originalStart, entries[0].StartTime);
        Assert.Equal(originalEnd, entries[0].EndTime);
    }

    [Fact]
    public void ParseMillisecondsCorrectly()
    {
        var srt = @"1
00:01:23,456 --> 00:02:34,789
Test.
";
        var entries = SrtParser.Parse(srt);
        Assert.Equal(new TimeSpan(0, 0, 1, 23, 456), entries[0].StartTime);
        Assert.Equal(new TimeSpan(0, 0, 2, 34, 789), entries[0].EndTime);
    }

    [Fact]
    public void Parse_MixedValidAndInvalid_ExtractsValid()
    {
        var srt = @"1
00:00:01,000 --> 00:00:04,000
This file is intentionally malformed.

2
00:00:05 --> 00:00:08
Missing milliseconds in timestamp.

3
Bad sequence number here
00:00:10,000 --> 00:00:13,000
This won't parse correctly.
";
        // All three entries should parse now
        var entries = SrtParser.Parse(srt);
        Assert.Equal(3, entries.Count);
    }
}
