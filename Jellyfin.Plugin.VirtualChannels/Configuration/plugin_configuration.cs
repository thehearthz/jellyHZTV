using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.VirtualChannels.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        Channels = new List<VirtualChannel>();
        CommercialLibraries = new List<string>();
        PreRollLibraries = new List<string>();
    }

    public List<VirtualChannel> Channels { get; set; }
    public List<string> CommercialLibraries { get; set; }
    public List<string> PreRollLibraries { get; set; }
    public bool EnableAutoGeneration { get; set; } = true;
}

public class VirtualChannel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public ChannelType Type { get; set; } = ChannelType.Manual;
    public List<ProgrammingBlock> Schedule { get; set; } = new();
    public CommercialSettings CommercialSettings { get; set; } = new();
    public bool Enabled { get; set; } = true;
    
    // Auto-generation settings
    public AutoGenerationSettings? AutoSettings { get; set; }
}

public enum ChannelType
{
    Manual,
    AutoGenre,
    AutoYear,
    AutoDecade,
    AutoRating,
    AutoActor,
    AutoDirector,
    AutoStudio
}

public class ProgrammingBlock
{
    public string Id { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public List<string> ContentIds { get; set; } = new();
    public ContentType ContentType { get; set; }
    public bool Shuffle { get; set; } = false;
    public bool RespectEpisodeOrder { get; set; } = true;
    public PlaybackMode Mode { get; set; } = PlaybackMode.Sequential;
}

public enum ContentType
{
    Movie,
    TvShow,
    Mixed,
    Commercial,
    PreRoll
}

public enum PlaybackMode
{
    Sequential,
    Shuffle,
    Random
}

public class CommercialSettings
{
    public bool EnableCommercials { get; set; } = true;
    public CommercialInterval Interval { get; set; } = CommercialInterval.Every15Minutes;
    public int CustomIntervalMinutes { get; set; } = 15;
    public int MinCommercials { get; set; } = 1;
    public int MaxCommercials { get; set; } = 3;
    public bool InsertAtNaturalBreaks { get; set; } = true;
    public bool UsePreRolls { get; set; } = true;
}

public enum CommercialInterval
{
    Every10Minutes,
    Every15Minutes,
    Every20Minutes,
    Every30Minutes,
    Custom,
    NaturalBreaksOnly
}

public class AutoGenerationSettings
{
    public List<string> Genres { get; set; } = new();
    public int? FromYear { get; set; }
    public int? ToYear { get; set; }
    public List<string> Studios { get; set; } = new();
    public List<string> Actors { get; set; } = new();
    public List<string> Directors { get; set; } = new();
    public string? Rating { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IncludeMovies { get; set; } = true;
    public bool IncludeTvShows { get; set; } = true;
}
