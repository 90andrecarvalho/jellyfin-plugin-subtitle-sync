using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleOffset.Services;
using Jellyfin.Plugin.SubtitleOffset.Srt;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOffset.Api;

/// <summary>
/// API controller for subtitle sync operations.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("SubtitleOffset")]
[Produces(MediaTypeNames.Application.Json)]
public class SubtitleOffsetController : ControllerBase
{
    private readonly OffsetFileService _offsetService;
    private readonly WaveformService _waveformService;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ILogger<SubtitleOffsetController> _logger;

    public SubtitleOffsetController(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        IProviderManager providerManager,
        MediaBrowser.Model.IO.IFileSystem fileSystem,
        IMediaEncoder mediaEncoder,
        IApplicationPaths applicationPaths,
        ILogger<SubtitleOffsetController> logger,
        ILogger<OffsetFileService> serviceLogger,
        ILogger<WaveformService> waveformLogger)
    {
        _offsetService = new OffsetFileService(libraryManager, mediaSourceManager, providerManager, fileSystem, serviceLogger);
        _waveformService = new WaveformService(mediaEncoder, waveformLogger, applicationPaths);
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _logger = logger;
    }

    /// <summary>
    /// Generates a new .srt file with the specified offset applied.
    /// </summary>
    [HttpPost("Generate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Generate([FromBody] GenerateRequest request)
    {
        if (request is null || request.ItemId == Guid.Empty)
        {
            return BadRequest(new { error = "itemId is required" });
        }

        try
        {
            var result = _offsetService.Generate(request.ItemId, request.SubtitleStreamIndex, request.OffsetMs);

            if (result.Deleted)
            {
                return NoContent();
            }

            return Ok(new
            {
                generatedFile = result.GeneratedFile,
                trackName = result.TrackName
            });
        }
        catch (ItemNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (SrtParseException)
        {
            return BadRequest(new { error = "Subtitle file is malformed" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogError(ex, "File system error generating subtitle file");
            return StatusCode(500, new { error = "Failed to write subtitle file: " + ex.Message });
        }
    }

    /// <summary>
    /// Replaces the original .srt file with offset-adjusted content.
    /// </summary>
    [HttpPost("ReplaceOriginal")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult ReplaceOriginal([FromBody] GenerateRequest request)
    {
        if (request is null || request.ItemId == Guid.Empty)
        {
            return BadRequest(new { error = "itemId is required" });
        }

        if (request.OffsetMs == 0)
        {
            return BadRequest(new { error = "Cannot replace original with zero offset" });
        }

        try
        {
            var result = _offsetService.ReplaceOriginal(request.ItemId, request.SubtitleStreamIndex, request.OffsetMs);

            return Ok(new
            {
                replacedFile = result.ReplacedFile,
                offsetApplied = result.OffsetApplied
            });
        }
        catch (ItemNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (SrtParseException)
        {
            return BadRequest(new { error = "Subtitle file is malformed" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogError(ex, "File system error replacing original subtitle");
            return StatusCode(500, new { error = "Failed to write subtitle file: " + ex.Message });
        }
    }

    /// <summary>
    /// Gets parsed subtitle content for the editor timeline.
    /// </summary>
    [HttpGet("SubtitleContent/{itemId}/{streamIndex}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetSubtitleContent(Guid itemId, int streamIndex)
    {
        try
        {
            var result = _offsetService.GetSubtitleContent(itemId, streamIndex);
            return Ok(result);
        }
        catch (ItemNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (SrtParseException)
        {
            return BadRequest(new { error = "Subtitle file is malformed" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Generates audio waveform data for a video.
    /// </summary>
    [HttpPost("GenerateWaveform")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GenerateWaveform([FromBody] WaveformRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.ItemId == Guid.Empty)
        {
            return BadRequest(new { error = "itemId is required" });
        }

        // Check cache first
        var cached = _waveformService.GetCachedWaveform(request.ItemId);
        if (cached is not null)
        {
            return Ok(new { status = "complete", sampleRate = cached.SampleRate, samples = cached.Samples });
        }

        // Resolve item to get media file path and duration
        var item = _libraryManager.GetItemById(request.ItemId) as Video;
        if (item is null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var mediaPath = item.Path;
        if (string.IsNullOrEmpty(mediaPath))
        {
            return BadRequest(new { error = "Item has no media file" });
        }

        var duration = item.RunTimeTicks.HasValue
            ? TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalSeconds
            : 0;

        if (duration <= 0)
        {
            return BadRequest(new { error = "Item has no duration information" });
        }

        try
        {
            var waveform = await _waveformService.GenerateAsync(mediaPath, duration, cancellationToken).ConfigureAwait(false);
            _waveformService.SaveCachedWaveform(request.ItemId, waveform);
            return Ok(new { status = "complete", sampleRate = waveform.SampleRate, samples = waveform.Samples, itemId = request.ItemId });
        }
        catch (OperationCanceledException)
        {
            return Ok(new { status = "cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate waveform for item {ItemId}", request.ItemId);
            return StatusCode(500, new { error = "Waveform generation failed: " + ex.Message });
        }
    }

    /// <summary>
    /// Gets cached waveform data for a video.
    /// </summary>
    [HttpGet("Waveform/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetWaveform(Guid itemId)
    {
        var cached = _waveformService.GetCachedWaveform(itemId);
        if (cached is null)
        {
            return NotFound(new { error = "No waveform data cached for this item" });
        }

        return Ok(new { sampleRate = cached.SampleRate, samples = cached.Samples });
    }

    /// <summary>
    /// Cancels an in-progress waveform generation and deletes any cached data.
    /// </summary>
    [HttpDelete("CancelWaveform")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult CancelWaveform([FromQuery] Guid itemId)
    {
        _waveformService.CancelGeneration();
        if (itemId != Guid.Empty)
        {
            _waveformService.DeleteCachedWaveform(itemId);
        }

        return Ok(new { status = "cancelled" });
    }
}

/// <summary>
/// Request model for Generate and ReplaceOriginal endpoints.
/// </summary>
public class GenerateRequest
{
    public Guid ItemId { get; set; }
    public int SubtitleStreamIndex { get; set; }
    public long OffsetMs { get; set; }
}

/// <summary>
/// Request model for waveform generation.
/// </summary>
public class WaveformRequest
{
    public Guid ItemId { get; set; }
}
