using System;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.SubtitleOffset.Srt;

/// <summary>
/// Detects the encoding of a file by examining BOM and byte patterns.
/// </summary>
public static class EncodingDetector
{
    /// <summary>
    /// Detects the encoding of a file. Falls back to UTF-8 if uncertain.
    /// </summary>
    public static Encoding Detect(byte[] fileBytes)
    {
        if (fileBytes.Length >= 3 && fileBytes[0] == 0xEF && fileBytes[1] == 0xBB && fileBytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (fileBytes.Length >= 2 && fileBytes[0] == 0xFF && fileBytes[1] == 0xFE)
        {
            return Encoding.Unicode; // UTF-16 LE
        }

        if (fileBytes.Length >= 2 && fileBytes[0] == 0xFE && fileBytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode; // UTF-16 BE
        }

        // Try to detect if it's valid UTF-8
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            utf8.GetString(fileBytes);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8, assume Latin-1 (ISO 8859-1)
            return Encoding.Latin1;
        }
    }
}
