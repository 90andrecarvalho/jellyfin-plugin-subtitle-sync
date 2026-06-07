using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOffset.Services;

public class WaveformService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<WaveformService> _logger;
    private readonly IApplicationPaths _applicationPaths;
    private readonly SemaphoreSlim _generationLock = new(1, 1);
    private readonly object _processLock = new();
    private Process? _currentProcess;

    public WaveformService(
        IMediaEncoder mediaEncoder,
        ILogger<WaveformService> logger,
        IApplicationPaths applicationPaths)
    {
        _mediaEncoder = mediaEncoder;
        _logger = logger;
        _applicationPaths = applicationPaths;
    }

    public Task<WaveformData> GenerateAsync(
        string mediaFilePath,
        double totalDurationSeconds,
        CancellationToken cancellationToken)
    {
        return GenerateAsync(mediaFilePath, totalDurationSeconds, null, cancellationToken);
    }

    public async Task<WaveformData> GenerateAsync(
        string mediaFilePath,
        double totalDurationSeconds,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath))
        {
            throw new ArgumentException("Media file path is required.", nameof(mediaFilePath));
        }

        if (!File.Exists(mediaFilePath))
        {
            throw new FileNotFoundException("Media file was not found.", mediaFilePath);
        }

        if (string.IsNullOrWhiteSpace(_mediaEncoder.EncoderPath))
        {
            throw new InvalidOperationException("FFmpeg path is not configured.");
        }

        await _generationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        Process? process = null;

        try
        {
            progress?.Report(0);

            var startInfo = new ProcessStartInfo
            {
                FileName = _mediaEncoder.EncoderPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-nostdin");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(mediaFilePath);
            startInfo.ArgumentList.Add("-vn");
            startInfo.ArgumentList.Add("-ac");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-ar");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("f32le");
            startInfo.ArgumentList.Add("-");

            process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start FFmpeg.");
            }

            SetCurrentProcess(process);
            using var cancellationRegistration = cancellationToken.Register(static state => ((WaveformService)state!).CancelGeneration(), this);

            var stderrTask = process.StandardError.ReadToEndAsync();
            var rawSamples = await ReadSamplesAsync(
                process.StandardOutput.BaseStream,
                totalDurationSeconds,
                progress,
                cancellationToken).ConfigureAwait(false);

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {stderr}");
            }

            var waveform = Normalize(rawSamples);
            progress?.Report(1.0);
            _logger.LogInformation("Generated waveform with {SampleCount} samples for {MediaFilePath}", waveform.Samples.Length, mediaFilePath);
            return waveform;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Waveform generation cancelled for {MediaFilePath}", mediaFilePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate waveform for {MediaFilePath}", mediaFilePath);
            throw;
        }
        finally
        {
            ClearCurrentProcess(process);
            process?.Dispose();
            _generationLock.Release();
        }
    }

    public WaveformData? GetCachedWaveform(Guid itemId)
    {
        var cachePath = GetCachePath(itemId);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(cachePath);
            return JsonSerializer.Deserialize<WaveformData>(stream, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cached waveform for {ItemId}", itemId);
            return null;
        }
    }

    public bool HasCachedWaveform(Guid itemId)
    {
        return File.Exists(GetCachePath(itemId));
    }

    public void SaveCachedWaveform(Guid itemId, WaveformData waveformData)
    {
        ArgumentNullException.ThrowIfNull(waveformData);

        var cacheDirectory = GetCacheDirectory();
        Directory.CreateDirectory(cacheDirectory);

        var cachePath = GetCachePath(itemId);
        using var stream = File.Create(cachePath);
        JsonSerializer.Serialize(stream, waveformData, JsonOptions);
    }

    public void CancelGeneration()
    {
        Process? process;
        lock (_processLock)
        {
            process = _currentProcess;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                _logger.LogInformation("Cancelling waveform generation process {ProcessId}", process.Id);
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel waveform generation process.");
        }
    }

    /// <summary>
    /// Deletes cached waveform data for an item if it exists.
    /// </summary>
    public void DeleteCachedWaveform(Guid itemId)
    {
        var cachePath = GetCachePath(itemId);
        try
        {
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
                _logger.LogInformation("Deleted cached waveform for item {ItemId}", itemId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete cached waveform for item {ItemId}", itemId);
        }
    }

    private async Task<List<float>> ReadSamplesAsync(
        Stream outputStream,
        double totalDurationSeconds,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var expectedSamples = totalDurationSeconds > 0
            ? Math.Max(1, (int)Math.Ceiling(totalDurationSeconds))
            : 1;
        var samples = new List<float>(expectedSamples);
        var buffer = new byte[sizeof(float)];

        while (true)
        {
            var bytesRead = await ReadSampleAsync(outputStream, buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            if (bytesRead < sizeof(float))
            {
                _logger.LogDebug("Ignoring incomplete waveform sample with {ByteCount} bytes.", bytesRead);
                break;
            }

            var sample = BitConverter.ToSingle(buffer, 0);
            samples.Add(float.IsFinite(sample) ? Math.Abs(sample) : 0f);
            progress?.Report(Math.Min((double)samples.Count / expectedSamples, 1.0));
        }

        return samples;
    }

    private static async Task<int> ReadSampleAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        return totalRead;
    }

    private static WaveformData Normalize(List<float> samples)
    {
        if (samples.Count == 0)
        {
            return new WaveformData();
        }

        var maxValue = 0f;
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = float.IsFinite(samples[i]) ? Math.Abs(samples[i]) : 0f;
            samples[i] = sample;
            if (sample > maxValue)
            {
                maxValue = sample;
            }
        }

        if (maxValue > 0f)
        {
            for (var i = 0; i < samples.Count; i++)
            {
                samples[i] = Math.Clamp(samples[i] / maxValue, 0f, 1f);
            }
        }

        return new WaveformData
        {
            Samples = samples.ToArray()
        };
    }

    private void SetCurrentProcess(Process process)
    {
        lock (_processLock)
        {
            _currentProcess = process;
        }
    }

    private void ClearCurrentProcess(Process? process)
    {
        lock (_processLock)
        {
            if (ReferenceEquals(_currentProcess, process))
            {
                _currentProcess = null;
            }
        }
    }

    private string GetCacheDirectory()
    {
        return Path.GetFullPath(Path.Combine(_applicationPaths.PluginConfigurationsPath, "..", "SubtitleOffset", "waveforms"));
    }

    private string GetCachePath(Guid itemId)
    {
        return Path.Combine(GetCacheDirectory(), itemId + ".json");
    }
}

public class WaveformData
{
    public int SampleRate { get; set; } = 1;

    public float[] Samples { get; set; } = Array.Empty<float>();
}
