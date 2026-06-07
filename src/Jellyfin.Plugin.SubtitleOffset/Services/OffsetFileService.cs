using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SubtitleOffset.Srt;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOffset.Services;

/// <summary>
/// Service that generates offset subtitle files on disk.
/// </summary>
public class OffsetFileService
{
    private static readonly Regex OffsetFilePattern = new(
        @"\.Offset[+-]\d+ms\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OffsetValuePattern = new(
        @"\.Offset([+-]\d+)ms\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<OffsetFileService> _logger;

    public OffsetFileService(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        ILogger<OffsetFileService> logger)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Determines if a subtitle filename is an offset-generated file.
    /// </summary>
    public static bool IsOffsetFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return OffsetFilePattern.IsMatch(Path.GetFileName(path));
    }

    /// <summary>
    /// Finds the original non-offset subtitle path for the specified video and language.
    /// </summary>
    public static string FindOriginalSubtitlePath(string videoDir, string videoBaseName, string lang)
    {
        var exactFileName = lang == "und"
            ? $"{videoBaseName}.srt"
            : $"{videoBaseName}.{lang}.srt";
        var exactPath = Path.Combine(videoDir, exactFileName);

        if (File.Exists(exactPath) && !IsOffsetFile(exactPath))
        {
            return exactPath;
        }

        var pattern = lang == "und"
            ? $"{videoBaseName}*.srt"
            : $"{videoBaseName}.{lang}*.srt";

        var originalPath = Directory
            .GetFiles(videoDir, pattern)
            .Where(file => !IsOffsetFile(file))
            .OrderBy(file => Path.GetFileName(file).Length)
            .ThenBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(originalPath))
        {
            return originalPath;
        }

        throw new InvalidOperationException($"Original subtitle file not found for language '{lang}'");
    }

    /// <summary>
    /// Gets the existing offset value for the specified video and language from an offset filename.
    /// </summary>
    public static long GetExistingOffsetMs(string videoDir, string videoBaseName, string lang)
    {
        var pattern = $"{videoBaseName}.{lang}.Offset*ms.srt";
        var existingOffsetFile = Directory
            .GetFiles(videoDir, pattern)
            .Where(IsOffsetFile)
            .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return string.IsNullOrEmpty(existingOffsetFile)
            ? 0
            : ExtractOffsetMsFromPath(existingOffsetFile);
    }

    /// <summary>
    /// Generates a new .srt file with the given offset applied.
    /// Returns the generated filename and display track name.
    /// </summary>
    public GenerateResult Generate(Guid itemId, int subtitleStreamIndex, long offsetMs)
    {
        ValidateOffsetRange(offsetMs);

        var (item, _, subtitlePath, videoDir, videoBaseName, lang) = ResolveSubtitleContext(itemId, subtitleStreamIndex);
        var originalSubtitlePath = IsOffsetFile(subtitlePath)
            ? FindOriginalSubtitlePath(videoDir, videoBaseName, lang)
            : subtitlePath;

        var (entries, encoding) = ReadSubtitleEntries(originalSubtitlePath);

        DeleteExistingOffsetFiles(videoDir, videoBaseName, lang);

        if (offsetMs == 0)
        {
            RefreshItem(item);
            return new GenerateResult
            {
                GeneratedFile = null,
                TrackName = null,
                Deleted = true
            };
        }

        SrtParser.ApplyOffset(entries, offsetMs);

        var offsetLabel = BuildOffsetLabel(offsetMs);
        var newFileName = $"{videoBaseName}.{lang}.{offsetLabel}.srt";
        var newFilePath = Path.Combine(videoDir, newFileName);

        WriteSubtitleEntries(newFilePath, entries, encoding);

        _logger.LogInformation("Generated offset subtitle from original {OriginalPath}: {Path}", originalSubtitlePath, newFilePath);

        RefreshItem(item);

        return new GenerateResult
        {
            GeneratedFile = newFileName,
            TrackName = $"{GetLanguageDisplayName(lang)} ({offsetLabel})",
            Deleted = false
        };
    }

    /// <summary>
    /// Replaces the original subtitle file with content adjusted by the specified offset.
    /// </summary>
    public ReplaceResult ReplaceOriginal(Guid itemId, int subtitleStreamIndex, long offsetMs)
    {
        ValidateOffsetRange(offsetMs);

        var (item, _, subtitlePath, videoDir, videoBaseName, lang) = ResolveSubtitleContext(itemId, subtitleStreamIndex);
        var originalSubtitlePath = IsOffsetFile(subtitlePath)
            ? FindOriginalSubtitlePath(videoDir, videoBaseName, lang)
            : subtitlePath;

        var (entries, encoding) = ReadSubtitleEntries(originalSubtitlePath);
        SrtParser.ApplyOffset(entries, offsetMs);
        WriteSubtitleEntries(originalSubtitlePath, entries, encoding);

        DeleteExistingOffsetFiles(videoDir, videoBaseName, lang);

        _logger.LogInformation("Replaced original subtitle with adjusted content: {Path}", originalSubtitlePath);

        RefreshItem(item);

        return new ReplaceResult
        {
            ReplacedFile = Path.GetFileName(originalSubtitlePath),
            OffsetApplied = offsetMs
        };
    }

    /// <summary>
    /// Gets subtitle content from the original subtitle file along with offset metadata.
    /// </summary>
    public SubtitleContentResult GetSubtitleContent(Guid itemId, int subtitleStreamIndex)
    {
        var (_, _, subtitlePath, videoDir, videoBaseName, lang) = ResolveSubtitleContext(itemId, subtitleStreamIndex);
        var isOffsetFile = IsOffsetFile(subtitlePath);
        var originalSubtitlePath = isOffsetFile
            ? FindOriginalSubtitlePath(videoDir, videoBaseName, lang)
            : subtitlePath;

        var existingOffsetMs = isOffsetFile
            ? ExtractOffsetMsFromPath(subtitlePath)
            : GetExistingOffsetMs(videoDir, videoBaseName, lang);

        var (entries, _) = ReadSubtitleEntries(originalSubtitlePath);

        return new SubtitleContentResult
        {
            Language = lang,
            IsOffsetFile = isOffsetFile,
            ExistingOffsetMs = existingOffsetMs,
            Entries = entries.Select(entry => new SubtitleEntryDto
            {
                Index = entry.SequenceNumber,
                StartMs = (long)entry.StartTime.TotalMilliseconds,
                EndMs = (long)entry.EndTime.TotalMilliseconds,
                Text = string.Join("\n", entry.TextLines)
            }).ToList()
        };
    }

    private (Video Item, MediaStream SubtitleStream, string SubtitlePath, string VideoDir, string VideoBaseName, string Language) ResolveSubtitleContext(Guid itemId, int subtitleStreamIndex)
    {
        var item = _libraryManager.GetItemById(itemId) as Video;
        if (item is null)
        {
            throw new ItemNotFoundException($"Item {itemId} not found or is not a video");
        }

        var subtitleStream = _mediaSourceManager
            .GetMediaStreams(itemId)
            .FirstOrDefault(s => s.Type == MediaStreamType.Subtitle && s.Index == subtitleStreamIndex);

        if (subtitleStream is null)
        {
            throw new ArgumentException($"Subtitle stream index {subtitleStreamIndex} not found");
        }

        if (!subtitleStream.IsExternal)
        {
            throw new InvalidOperationException("Only external subtitle files are supported");
        }

        var subtitlePath = subtitleStream.Path;
        if (string.IsNullOrEmpty(subtitlePath) || !subtitlePath.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .srt subtitle files are supported");
        }

        if (string.IsNullOrEmpty(item.Path))
        {
            throw new InvalidOperationException($"Video item {itemId} does not have a valid file path");
        }

        var videoDir = Path.GetDirectoryName(item.Path);
        if (string.IsNullOrEmpty(videoDir))
        {
            throw new InvalidOperationException($"Video item {itemId} does not have a valid directory path");
        }

        var videoBaseName = Path.GetFileNameWithoutExtension(item.Path);
        var lang = ExtractLanguageCode(subtitlePath, videoBaseName);

        return (item, subtitleStream, subtitlePath, videoDir, videoBaseName, lang);
    }

    private static (List<SrtEntry> Entries, Encoding Encoding) ReadSubtitleEntries(string subtitlePath)
    {
        var fileBytes = File.ReadAllBytes(subtitlePath);
        var encoding = EncodingDetector.Detect(fileBytes);
        var content = encoding.GetString(fileBytes);

        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        return (SrtParser.Parse(content), encoding);
    }

    private static void WriteSubtitleEntries(string subtitlePath, List<SrtEntry> entries, Encoding encoding)
    {
        var srtContent = SrtWriter.Write(entries);
        File.WriteAllText(subtitlePath, srtContent, encoding);
    }

    private static string BuildOffsetLabel(long offsetMs)
    {
        var sign = offsetMs >= 0 ? "+" : string.Empty;
        return $"Offset{sign}{offsetMs}ms";
    }

    private static long ExtractOffsetMsFromPath(string path)
    {
        var match = OffsetValuePattern.Match(Path.GetFileName(path));
        return match.Success && long.TryParse(match.Groups[1].Value, out var offsetMs)
            ? offsetMs
            : 0;
    }

    private void DeleteExistingOffsetFiles(string videoDir, string videoBaseName, string lang)
    {
        var pattern = $"{videoBaseName}.{lang}.Offset*ms.srt";
        var existingFiles = Directory.GetFiles(videoDir, pattern);

        foreach (var file in existingFiles)
        {
            if (IsOffsetFile(file))
            {
                File.Delete(file);
                _logger.LogInformation("Deleted previous offset file: {Path}", file);
            }
        }
    }

    private void RefreshItem(BaseItem item)
    {
        try
        {
            _providerManager.QueueRefresh(
                item.Id,
                new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    ImageRefreshMode = MetadataRefreshMode.None,
                    ReplaceAllMetadata = false,
                    IsAutomated = true
                },
                RefreshPriority.High);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue metadata refresh for item {ItemId}", item.Id);
        }
    }

    private static void ValidateOffsetRange(long offsetMs)
    {
        var maxOffset = Plugin.Instance?.Configuration?.MaxOffsetMs ?? 600_000;
        if (Math.Abs(offsetMs) > maxOffset)
        {
            throw new ArgumentException($"Offset exceeds maximum allowed value of {maxOffset}ms");
        }
    }

    private static string ExtractLanguageCode(string subtitlePath, string videoBaseName)
    {
        // Subtitle is named like: VideoName.en.srt or VideoName.en.SDH.srt
        var subFileName = Path.GetFileNameWithoutExtension(subtitlePath);

        // Remove video base name prefix
        if (subFileName.StartsWith(videoBaseName, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = subFileName[videoBaseName.Length..].TrimStart('.');
            var parts = remainder.Split('.');
            if (parts.Length > 0 && parts[0].Length is 2 or 3)
            {
                return parts[0];
            }
        }

        return "und"; // undetermined
    }

    private static string GetLanguageDisplayName(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "en" => "English",
            "es" => "Spanish",
            "fr" => "French",
            "de" => "German",
            "it" => "Italian",
            "pt" => "Portuguese",
            "ja" => "Japanese",
            "ko" => "Korean",
            "zh" => "Chinese",
            "ar" => "Arabic",
            "ru" => "Russian",
            "nl" => "Dutch",
            "sv" => "Swedish",
            "no" => "Norwegian",
            "da" => "Danish",
            "fi" => "Finnish",
            "pl" => "Polish",
            "tr" => "Turkish",
            "und" => "Unknown",
            _ => code.ToUpperInvariant()
        };
    }
}

/// <summary>
/// Result of a generate operation.
/// </summary>
public class GenerateResult
{
    public string? GeneratedFile { get; set; }
    public string? TrackName { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Result of replacing the original subtitle file.
/// </summary>
public class ReplaceResult
{
    /// <summary>
    /// Gets or sets the replaced subtitle filename.
    /// </summary>
    public string ReplacedFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the offset that was applied.
    /// </summary>
    public long OffsetApplied { get; set; }
}

/// <summary>
/// Result containing subtitle content and offset metadata.
/// </summary>
public class SubtitleContentResult
{
    /// <summary>
    /// Gets or sets the subtitle language.
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// Gets or sets a value indicating whether the selected stream is an offset file.
    /// </summary>
    public bool IsOffsetFile { get; set; }

    /// <summary>
    /// Gets or sets the existing offset in milliseconds.
    /// </summary>
    public long ExistingOffsetMs { get; set; }

    /// <summary>
    /// Gets or sets the subtitle entries.
    /// </summary>
    public List<SubtitleEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// DTO for subtitle entry content.
/// </summary>
public class SubtitleEntryDto
{
    /// <summary>
    /// Gets or sets the subtitle index.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the start time in milliseconds.
    /// </summary>
    public long StartMs { get; set; }

    /// <summary>
    /// Gets or sets the end time in milliseconds.
    /// </summary>
    public long EndMs { get; set; }

    /// <summary>
    /// Gets or sets the subtitle text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Thrown when an item is not found.
/// </summary>
public class ItemNotFoundException : Exception
{
    public ItemNotFoundException(string message) : base(message) { }
}
