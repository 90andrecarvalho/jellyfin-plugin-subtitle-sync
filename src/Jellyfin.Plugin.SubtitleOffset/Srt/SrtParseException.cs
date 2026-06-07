using System;

namespace Jellyfin.Plugin.SubtitleOffset.Srt;

/// <summary>
/// Exception thrown when .srt parsing fails.
/// </summary>
public class SrtParseException : Exception
{
    public SrtParseException(string message) : base(message) { }
    public SrtParseException(string message, Exception inner) : base(message, inner) { }
}
