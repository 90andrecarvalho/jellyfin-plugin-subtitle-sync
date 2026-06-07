using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SubtitleOffset.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubtitleOffset;

/// <summary>
/// Main plugin class for Subtitle Sync.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Subtitle Sync";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("b3c4d5e6-f7a8-9012-bcde-f34567890abc");

    /// <inheritdoc />
    public override string Description =>
        "Generates corrected .srt files with adjusted timing so subtitle fixes apply to all users.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
