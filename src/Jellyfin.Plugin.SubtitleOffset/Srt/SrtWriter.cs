using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.SubtitleOffset.Srt;

/// <summary>
/// Writes a list of <see cref="SrtEntry"/> back to .srt format.
/// </summary>
public static class SrtWriter
{
    /// <summary>
    /// Converts entries to .srt formatted string.
    /// </summary>
    public static string Write(List<SrtEntry> entries)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            sb.AppendLine(entry.SequenceNumber.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine($"{FormatTimestamp(entry.StartTime)} --> {FormatTimestamp(entry.EndTime)}");

            foreach (var line in entry.TextLines)
            {
                sb.AppendLine(line);
            }

            // Blank line between entries (except after last)
            if (i < entries.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string FormatTimestamp(TimeSpan ts)
    {
        int hours = (int)ts.TotalHours;
        int minutes = ts.Minutes;
        int seconds = ts.Seconds;
        int milliseconds = ts.Milliseconds;

        return $"{hours:D2}:{minutes:D2}:{seconds:D2},{milliseconds:D3}";
    }
}
