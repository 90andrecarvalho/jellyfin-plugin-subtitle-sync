using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SubtitleOffset.Srt;

/// <summary>
/// Parses .srt subtitle files into a list of <see cref="SrtEntry"/>.
/// </summary>
public static class SrtParser
{
    // Lenient timestamp: accepts 1-2 digit hours, , or . as ms separator, optional ms, trailing text
    private static readonly Regex TimestampRegex = new(
        @"^\s*(\d{1,2}):(\d{2}):(\d{2})(?:[,.](\d{1,3}))?\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})(?:[,.](\d{1,3}))?\s*",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses .srt content into entries. Only throws <see cref="SrtParseException"/> if no entries found.
    /// Lines with invalid timestamps or missing sequence numbers are skipped.
    /// </summary>
    public static List<SrtEntry> Parse(string content)
    {
        var entries = new List<SrtEntry>();
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        int i = 0;
        int entryCount = 0;

        while (i < lines.Length)
        {
            // Skip blank lines
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
            {
                i++;
            }

            if (i >= lines.Length)
            {
                break;
            }

            // Try to find a timestamp line. It might be on the current line (no sequence number)
            // or on the next line (with a sequence number before it).
            int seqNum;
            int timestampLine;

            var match = TimestampRegex.Match(lines[i]);
            if (match.Success)
            {
                // Current line is a timestamp (no sequence number)
                entryCount++;
                seqNum = entryCount;
                timestampLine = i;
            }
            else
            {
                // Try to interpret current line as sequence number, next as timestamp
                int.TryParse(lines[i].Trim(), out seqNum);
                i++;

                // Find the timestamp line, skipping any non-timestamp lines
                while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                {
                    i++;
                }

                if (i >= lines.Length)
                {
                    break;
                }

                match = TimestampRegex.Match(lines[i]);
                if (!match.Success)
                {
                    // Not a valid entry — skip this line and continue scanning
                    i++;
                    continue;
                }

                timestampLine = i;
                if (seqNum == 0)
                {
                    entryCount++;
                    seqNum = entryCount;
                }
                else
                {
                    entryCount = seqNum;
                }
            }

            var startTime = ParseTimestamp(match, 1);
            var endTime = ParseTimestamp(match, 5);

            i = timestampLine + 1;

            // Parse text lines until empty line or end of file
            var textLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                textLines.Add(lines[i]);
                i++;
            }

            // Skip entries with no text content
            if (textLines.Count == 0)
            {
                continue;
            }

            entries.Add(new SrtEntry
            {
                SequenceNumber = seqNum,
                StartTime = startTime,
                EndTime = endTime,
                TextLines = textLines.ToArray()
            });
        }

        if (entries.Count == 0)
        {
            throw new SrtParseException("No valid subtitle entries found");
        }

        return entries;
    }

    /// <summary>
    /// Applies an offset to all entries, clamping negative results to TimeSpan.Zero.
    /// </summary>
    public static void ApplyOffset(List<SrtEntry> entries, long offsetMs)
    {
        var offset = TimeSpan.FromMilliseconds(offsetMs);

        foreach (var entry in entries)
        {
            entry.StartTime = ClampToZero(entry.StartTime + offset);
            entry.EndTime = ClampToZero(entry.EndTime + offset);
        }
    }

    private static TimeSpan ParseTimestamp(Match match, int groupOffset)
    {
        int hours = int.Parse(match.Groups[groupOffset].Value);
        int minutes = int.Parse(match.Groups[groupOffset + 1].Value);
        int seconds = int.Parse(match.Groups[groupOffset + 2].Value);
        int ms = 0;

        if (match.Groups[groupOffset + 3].Success && match.Groups[groupOffset + 3].Value.Length > 0)
        {
            var msStr = match.Groups[groupOffset + 3].Value;
            // Pad to 3 digits (e.g., "5" → "500", "50" → "500")
            ms = int.Parse(msStr.PadRight(3, '0'));
        }

        return new TimeSpan(0, hours, minutes, seconds, ms);
    }

    private static TimeSpan ClampToZero(TimeSpan value)
        => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
