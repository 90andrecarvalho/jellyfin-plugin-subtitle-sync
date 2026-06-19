using System;

namespace Jellyfin.Plugin.SubtitleOffset.Srt;

/// <summary>
/// Exception thrown when .srt parsing fails.
/// </summary>
public class SrtParseException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SrtParseException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SrtParseException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SrtParseException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    public SrtParseException(string message, Exception inner) : base(message, inner) { }
}
