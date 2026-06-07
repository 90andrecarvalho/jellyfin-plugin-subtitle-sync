using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitleOffset.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the maximum allowed offset in milliseconds (absolute value).
    /// Default: 600000 (10 minutes).
    /// </summary>
    public int MaxOffsetMs { get; set; } = 600_000;
}
