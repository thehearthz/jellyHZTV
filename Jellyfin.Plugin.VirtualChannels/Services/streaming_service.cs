using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.VirtualChannels.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VirtualChannels.Services;

public class StreamingService
{
    private readonly ILogger<StreamingService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly ChannelManager _channelManager;
    private readonly Dictionary<string, ChannelPlaybackState> _playbackStates = new();

    public StreamingService(
        ILogger<StreamingService> logger,
        ILibraryManager libraryManager,
        ChannelManager channelManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _channelManager = channelManager;
    }

    public async Task<PlaybackItem> GetCurrentPlaybackItem(string channelId, DateTime currentTime)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
            return null;

        var channel = config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel == null)
            return null;

        // Get or create playback state
        if (!_playbackStates.ContainsKey(channelId))
        {
            _playbackStates[channelId] = new ChannelPlaybackState(channel);
        }

        var state = _playbackStates[channelId];
        
        // Get content for the current time
        var content = _channelManager.GetContentForChannel(channel, currentTime);
        if (!content.Any())
            return null;

        // Determine what should be playing
        var playbackItem = await DeterminePlaybackItem(channel, state, content, currentTime);
        
        return playbackItem;
    }

    private async Task<PlaybackItem> DeterminePlaybackItem(
        VirtualChannel channel,
        ChannelPlaybackState state,
        List<BaseItem> content,
        DateTime currentTime)
    {
        // Check if we should be in a commercial break
        if (ShouldPlayCommercial(channel, state, currentTime))
        {
            var commercial = GetNextCommercial(channel, state);
            if (commercial != null)
            {
                return new PlaybackItem
                {
                    Item = commercial,
                    StartTime = currentTime,
                    IsCommercial = true,
                    Position = TimeSpan.Zero
                };
            }
        }

        // Check if we should play a pre-roll
        if (state.ShouldPlayPreRoll && channel.CommercialSettings.UsePreRolls)
        {
            var preRoll = GetNextPreRoll(channel, state);
            if (preRoll != null)
            {
                state.ShouldPlayPreRoll = false;
                return new PlaybackItem
                {
                    Item = preRoll,
                    StartTime = currentTime,
                    IsPreRoll = true,
                    Position = TimeSpan.Zero
                };
            }
        }

        // Get current programming block
        var currentBlock = GetCurrentProgrammingBlock(channel, currentTime);
        if (currentBlock == null)
            return null;

        // Get content based on block settings
        List<BaseItem> blockContent;
        if (currentBlock.Shuffle || currentBlock.Mode == PlaybackMode.Shuffle)
        {
            blockContent = content.OrderBy(x => Guid.NewGuid()).ToList();
        }
        else if (currentBlock.Mode == PlaybackMode.Random)
        {
            var random = new Random();
            var randomItem = content[random.Next(content.Count)];
            blockContent = new List<BaseItem> { randomItem };
        }
        else
        {
            blockContent = content;
        }

        // Get current item from the block
        var currentItem = GetCurrentContentItem(blockContent, state, currentBlock);
        if (currentItem == null)
            return null;

        // Calculate position within the item
        var position = CalculatePosition(currentItem, state, currentTime);

        state.CurrentItem = currentItem;
        state.LastCommercialCheck = currentTime;
        state.ShouldPlayPreRoll = false;

        return new PlaybackItem
        {
            Item = currentItem,
            StartTime = currentTime,
            Position = position,
            IsCommercial = false,
            IsPreRoll = false
        };
    }

    private bool ShouldPlayCommercial(VirtualChannel channel, ChannelPlaybackState state, DateTime currentTime)
    {
        if (!channel.CommercialSettings.EnableCommercials)
            return false;

        if (state.InCommercialBreak)
            return true;

        if (state.LastCommercialCheck == DateTime.MinValue)
            return false;

        var timeSinceLastCheck = currentTime - state.LastCommercialCheck;
        
        int intervalMinutes = channel.CommercialSettings.Interval switch
        {
            CommercialInterval.Every10Minutes => 10,
            CommercialInterval.Every15Minutes => 15,
            CommercialInterval.Every20Minutes => 20,
            CommercialInterval.Every30Minutes => 30,
            CommercialInterval.Custom => channel.CommercialSettings.CustomIntervalMinutes,
            _ => 15
        };

        if (timeSinceLastCheck.TotalMinutes >= intervalMinutes)
        {
            state.InCommercialBreak = true;
            state.CommercialBreakCount = 0;
            return true;
        }

        return false;
    }

    private BaseItem GetNextCommercial(VirtualChannel channel, ChannelPlaybackState state)
    {
        var commercials = _channelManager.GetCommercials();
        if (!commercials.Any())
        {
            state.InCommercialBreak = false;
            return null;
        }

        // Check if we've played enough commercials
        if (state.CommercialBreakCount >= channel.CommercialSettings.MaxCommercials)
        {
            state.InCommercialBreak = false;
            state.CommercialBreakCount = 0;
            return null;
        }

        var random = new Random();
        var commercial = commercials[random.Next(commercials.Count)];
        state.CommercialBreakCount++;

        return commercial;
    }

    private BaseItem GetNextPreRoll(VirtualChannel channel, ChannelPlaybackState state)
    {
        var preRolls = _channelManager.GetPreRolls();
        if (!preRolls.Any())
            return null;

        var random = new Random();
        return preRolls[random.Next(preRolls.Count)];
    }

    private ProgrammingBlock GetCurrentProgrammingBlock(VirtualChannel channel, DateTime currentTime)
    {
        var timeOfDay = currentTime.TimeOfDay;

        return channel.Schedule
            .Where(block => block.StartTime <= timeOfDay &&
                          (!block.EndTime.HasValue || block.EndTime.Value > timeOfDay))
            .OrderBy(block => block.StartTime)
            .FirstOrDefault();
    }

    private BaseItem GetCurrentContentItem(List<BaseItem> content, ChannelPlaybackState state, ProgrammingBlock block)
    {
        if (!content.Any())
            return null;

        // If we don't have a current index, start at 0
        if (state.CurrentContentIndex < 0 || state.CurrentContentIndex >= content.Count)
        {
            state.CurrentContentIndex = 0;
        }

        var item = content[state.CurrentContentIndex];

        // Handle TV series - get specific episode
        if (item is MediaBrowser.Controller.Entities.TV.Series series && block.RespectEpisodeOrder)
        {
            return GetNextEpisode(series, state);
        }

        return item;
    }

    private BaseItem GetNextEpisode(MediaBrowser.Controller.Entities.TV.Series series, ChannelPlaybackState state)
    {
        var episodes = _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = series.Id,
            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Episode },
            Recursive = true,
            OrderBy = new[] 
            { 
                (ItemSortBy.ParentIndexNumber, MediaBrowser.Model.Entities.SortOrder.Ascending),
                (ItemSortBy.IndexNumber, MediaBrowser.Model.Entities.SortOrder.Ascending)
            }
        });

        if (!episodes.Any())
            return null;

        // Get current episode index for this series
        if (!state.SeriesEpisodeIndices.ContainsKey(series.Id.ToString()))
        {
            state.SeriesEpisodeIndices[series.Id.ToString()] = 0;
        }

        var episodeIndex = state.SeriesEpisodeIndices[series.Id.ToString()];
        if (episodeIndex >= episodes.Count)
        {
            // Loop back to first episode
            episodeIndex = 0;
            state.SeriesEpisodeIndices[series.Id.ToString()] = 0;
        }

        var episode = episodes[episodeIndex];
        
        // Increment for next time
        state.SeriesEpisodeIndices[series.Id.ToString()]++;

        return episode;
    }

    private TimeSpan CalculatePosition(BaseItem item, ChannelPlaybackState state, DateTime currentTime)
    {
        // For now, start from beginning
        // In a more advanced implementation, this would calculate the exact position
        // based on when the item started playing
        return TimeSpan.Zero;
    }

    public void AdvanceChannel(string channelId)
    {
        if (_playbackStates.ContainsKey(channelId))
        {
            var state = _playbackStates[channelId];
            state.CurrentContentIndex++;
            state.ShouldPlayPreRoll = true;
        }
    }
}

public class ChannelPlaybackState
{
    public ChannelPlaybackState(VirtualChannel channel)
    {
        Channel = channel;
        CurrentContentIndex = 0;
        SeriesEpisodeIndices = new Dictionary<string, int>();
        LastCommercialCheck = DateTime.MinValue;
        InCommercialBreak = false;
        CommercialBreakCount = 0;
        ShouldPlayPreRoll = true;
    }

    public VirtualChannel Channel { get; set; }
    public int CurrentContentIndex { get; set; }
    public BaseItem CurrentItem { get; set; }
    public Dictionary<string, int> SeriesEpisodeIndices { get; set; }
    public DateTime LastCommercialCheck { get; set; }
    public bool InCommercialBreak { get; set; }
    public int CommercialBreakCount { get; set; }
    public bool ShouldPlayPreRoll { get; set; }
}

public class PlaybackItem
{
    public BaseItem Item { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan Position { get; set; }
    public bool IsCommercial { get; set; }
    public bool IsPreRoll { get; set; }
}