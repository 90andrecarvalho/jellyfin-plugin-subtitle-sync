using System;

namespace Jellyfin.Plugin.SubtitleOffset.Srt;

/// <summary>
/// Represents a single subtitle entry in an .srt file.
/// </summary>
public class SrtEntry
{
    /// <summary>
    /// Gets or sets the sequence number.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>
    /// Gets or sets the end time.
    /// </summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>
    /// Gets or sets the subtitle text lines.
    /// </summary>
    public string[] TextLines { get; set; } = Array.Empty<string>();
}
