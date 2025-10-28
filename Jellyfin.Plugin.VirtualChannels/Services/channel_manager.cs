using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.VirtualChannels.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VirtualChannels.Services;

public class ChannelManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ChannelManager> _logger;

    public ChannelManager(ILibraryManager libraryManager, ILogger<ChannelManager> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public async Task<List<VirtualChannel>> GenerateAutoChannels()
    {
        var channels = new List<VirtualChannel>();

        try
        {
            // Generate genre-based channels
            channels.AddRange(await GenerateGenreChannels());

            // Generate decade-based channels
            channels.AddRange(await GenerateDecadeChannels());

            // Generate year-based channels
            channels.AddRange(await GenerateYearChannels());

            _logger.LogInformation("Generated {Count} automatic channels", channels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating automatic channels");
        }

        return channels;
    }

    private async Task<List<VirtualChannel>> GenerateGenreChannels()
    {
        var channels = new List<VirtualChannel>();
        var allItems = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        });

        var genreGroups = allItems
            .SelectMany(item => item.Genres)
            .Distinct()
            .OrderBy(g => g);

        int channelNumber = 100;
        foreach (var genre in genreGroups)
        {
            var genreItems = allItems
                .Where(item => item.Genres.Contains(genre))
                .ToList();

            if (genreItems.Count < 5) continue; // Skip genres with too few items

            var channel = new VirtualChannel
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"{genre} Channel",
                Number = channelNumber.ToString(),
                Type = ChannelType.AutoGenre,
                AutoSettings = new AutoGenerationSettings
                {
                    Genres = new List<string> { genre },
                    IncludeMovies = true,
                    IncludeTvShows = true
                },
                CommercialSettings = new CommercialSettings
                {
                    EnableCommercials = true,
                    Interval = CommercialInterval.Every15Minutes,
                    MinCommercials = 2,
                    MaxCommercials = 4
                }
            };

            // Create a 24-hour schedule
            channel.Schedule = CreateDefaultSchedule(genreItems);

            channels.Add(channel);
            channelNumber++;
        }

        return channels;
    }

    private async Task<List<VirtualChannel>> GenerateDecadeChannels()
    {
        var channels = new List<VirtualChannel>();
        var allItems = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        });

        var decades = new[] { 1950, 1960, 1970, 1980, 1990, 2000, 2010, 2020 };
        int channelNumber = 200;

        foreach (var decade in decades)
        {
            var decadeItems = allItems
                .Where(item => item.ProductionYear >= decade && item.ProductionYear < decade + 10)
                .ToList();

            if (decadeItems.Count < 5) continue;

            var channel = new VirtualChannel
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"{decade}s Classics",
                Number = channelNumber.ToString(),
                Type = ChannelType.AutoDecade,
                AutoSettings = new AutoGenerationSettings
                {
                    FromYear = decade,
                    ToYear = decade + 9,
                    IncludeMovies = true,
                    IncludeTvShows = true
                },
                CommercialSettings = new CommercialSettings
                {
                    EnableCommercials = true,
                    Interval = CommercialInterval.Every20Minutes,
                    MinCommercials = 2,
                    MaxCommercials = 3
                }
            };

            channel.Schedule = CreateDefaultSchedule(decadeItems);

            channels.Add(channel);
            channelNumber++;
        }

        return channels;
    }

    private async Task<List<VirtualChannel>> GenerateYearChannels()
    {
        var channels = new List<VirtualChannel>();
        var currentYear = DateTime.Now.Year;
        
        // Generate channels for recent years (last 5 years)
        int channelNumber = 300;
        for (int year = currentYear - 5; year <= currentYear; year++)
        {
            var yearItems = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Years = new[] { year },
                Recursive = true
            });

            if (yearItems.Count < 5) continue;

            var channel = new VirtualChannel
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"{year} Releases",
                Number = channelNumber.ToString(),
                Type = ChannelType.AutoYear,
                AutoSettings = new AutoGenerationSettings
                {
                    FromYear = year,
                    ToYear = year,
                    IncludeMovies = true,
                    IncludeTvShows = true
                },
                CommercialSettings = new CommercialSettings
                {
                    EnableCommercials = true,
                    Interval = CommercialInterval.Every15Minutes
                }
            };

            channel.Schedule = CreateDefaultSchedule(yearItems);

            channels.Add(channel);
            channelNumber++;
        }

        return channels;
    }

    private List<ProgrammingBlock> CreateDefaultSchedule(List<BaseItem> items)
    {
        var schedule = new List<ProgrammingBlock>();

        // Create 4 programming blocks (6 hours each)
        for (int i = 0; i < 4; i++)
        {
            var block = new ProgrammingBlock
            {
                Id = Guid.NewGuid().ToString(),
                StartTime = TimeSpan.FromHours(i * 6),
                EndTime = TimeSpan.FromHours((i + 1) * 6),
                ContentIds = items.Select(item => item.Id.ToString()).ToList(),
                ContentType = ContentType.Mixed,
                Shuffle = false,
                RespectEpisodeOrder = true,
                Mode = PlaybackMode.Sequential
            };

            schedule.Add(block);
        }

        return schedule;
    }

    public List<BaseItem> GetContentForChannel(VirtualChannel channel, DateTime currentTime)
    {
        var items = new List<BaseItem>();

        if (channel.AutoSettings != null)
        {
            // Build query based on auto-generation settings
            var query = new InternalItemsQuery
            {
                Recursive = true
            };

            var includeTypes = new List<BaseItemKind>();
            if (channel.AutoSettings.IncludeMovies)
                includeTypes.Add(BaseItemKind.Movie);
            if (channel.AutoSettings.IncludeTvShows)
                includeTypes.Add(BaseItemKind.Series);

            query.IncludeItemTypes = includeTypes.ToArray();

            if (channel.AutoSettings.Genres.Any())
                query.Genres = channel.AutoSettings.Genres.ToArray();

            if (channel.AutoSettings.FromYear.HasValue)
                query.MinPremiereDate = new DateTime(channel.AutoSettings.FromYear.Value, 1, 1);

            if (channel.AutoSettings.ToYear.HasValue)
                query.MaxPremiereDate = new DateTime(channel.AutoSettings.ToYear.Value, 12, 31);

            items = _libraryManager.GetItemList(query);
        }
        else
        {
            // Get content from manual schedule
            var currentBlock = GetCurrentProgrammingBlock(channel, currentTime);
            if (currentBlock != null)
            {
                items = currentBlock.ContentIds
                    .Select(id => _libraryManager.GetItemById(Guid.Parse(id)))
                    .Where(item => item != null)
                    .ToList()!;
            }
        }

        return items;
    }

    private ProgrammingBlock? GetCurrentProgrammingBlock(VirtualChannel channel, DateTime currentTime)
    {
        var timeOfDay = currentTime.TimeOfDay;

        return channel.Schedule
            .Where(block => block.StartTime <= timeOfDay && 
                          (!block.EndTime.HasValue || block.EndTime.Value > timeOfDay))
            .OrderBy(block => block.StartTime)
            .FirstOrDefault();
    }

    public List<BaseItem> GetCommercials()
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.CommercialLibraries == null || !config.CommercialLibraries.Any())
            return new List<BaseItem>();

        var items = new List<BaseItem>();
        foreach (var libraryId in config.CommercialLibraries)
        {
            var library = _libraryManager.GetItemById(Guid.Parse(libraryId));
            if (library != null)
            {
                var commercials = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Parent = library,
                    Recursive = true,
                    IncludeItemTypes = new[] { BaseItemKind.Video }
                });
                items.AddRange(commercials);
            }
        }

        return items;
    }

    public List<BaseItem> GetPreRolls()
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.PreRollLibraries == null || !config.PreRollLibraries.Any())
            return new List<BaseItem>();

        var items = new List<BaseItem>();
        foreach (var libraryId in config.PreRollLibraries)
        {
            var library = _libraryManager.GetItemById(Guid.Parse(libraryId));
            if (library != null)
            {
                var preRolls = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Parent = library,
                    Recursive = true,
                    IncludeItemTypes = new[] { BaseItemKind.Video }
                });
                items.AddRange(preRolls);
            }
        }

        return items;
    }
}
