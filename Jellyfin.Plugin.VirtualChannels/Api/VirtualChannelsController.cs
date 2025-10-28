using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.VirtualChannels.Configuration;
using Jellyfin.Plugin.VirtualChannels.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VirtualChannels.Api;

[ApiController]
[Route("VirtualChannels")]
[Produces(MediaTypeNames.Application.Json)]
public class VirtualChannelsController : ControllerBase
{
    private readonly ILogger<VirtualChannelsController> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly ChannelManager _channelManager;

    public VirtualChannelsController(
        ILogger<VirtualChannelsController> logger,
        ILibraryManager libraryManager,
        ChannelManager channelManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _channelManager = channelManager;
    }

    /// <summary>
    /// Gets all virtual channels.
    /// </summary>
    [HttpGet("Channels")]
    [Authorize(Policy = "DefaultAuthorization")]
    public ActionResult<List<VirtualChannel>> GetChannels()
    {
        var config = Plugin.Instance?.Configuration;
        return config?.Channels ?? new List<VirtualChannel>();
    }

    /// <summary>
    /// Gets a specific virtual channel.
    /// </summary>
    [HttpGet("Channels/{channelId}")]
    [Authorize(Policy = "DefaultAuthorization")]
    public ActionResult<VirtualChannel> GetChannel([FromRoute] string channelId)
    {
        var config = Plugin.Instance?.Configuration;
        var channel = config?.Channels.FirstOrDefault(c => c.Id == channelId);
        
        if (channel == null)
            return NotFound();

        return channel;
    }

    /// <summary>
    /// Creates a new virtual channel.
    /// </summary>
    [HttpPost("Channels")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult<VirtualChannel> CreateChannel([FromBody] VirtualChannel channel)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
            return BadRequest("Plugin not configured");

        channel.Id = Guid.NewGuid().ToString();
        config.Channels.Add(channel);
        Plugin.Instance.SaveConfiguration();

        _logger.LogInformation("Created virtual channel: {Name} ({Number})", channel.Name, channel.Number);

        return CreatedAtAction(nameof(GetChannel), new { channelId = channel.Id }, channel);
    }

    /// <summary>
    /// Updates an existing virtual channel.
    /// </summary>
    [HttpPut("Channels/{channelId}")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult<VirtualChannel> UpdateChannel([FromRoute] string channelId, [FromBody] VirtualChannel channel)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
            return BadRequest("Plugin not configured");

        var existingIndex = config.Channels.FindIndex(c => c.Id == channelId);
        if (existingIndex == -1)
            return NotFound();

        channel.Id = channelId;
        config.Channels[existingIndex] = channel;
        Plugin.Instance.SaveConfiguration();

        _logger.LogInformation("Updated virtual channel: {Name} ({Number})", channel.Name, channel.Number);

        return channel;
    }

    /// <summary>
    /// Deletes a virtual channel.
    /// </summary>
    [HttpDelete("Channels/{channelId}")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult DeleteChannel([FromRoute] string channelId)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
            return BadRequest("Plugin not configured");

        var channel = config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel == null)
            return NotFound();

        config.Channels.Remove(channel);
        Plugin.Instance.SaveConfiguration();

        _logger.LogInformation("Deleted virtual channel: {Name} ({Number})", channel.Name, channel.Number);

        return NoContent();
    }

    /// <summary>
    /// Generates automatic channels.
    /// </summary>
    [HttpPost("GenerateAutoChannels")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<ActionResult<List<VirtualChannel>>> GenerateAutoChannels()
    {
        try
        {
            var channels = await _channelManager.GenerateAutoChannels();
            
            var config = Plugin.Instance?.Configuration;
            if (config == null)
                return BadRequest("Plugin not configured");

            // Remove existing auto-generated channels
            config.Channels.RemoveAll(c => c.Type != ChannelType.Manual);

            // Add new auto-generated channels
            config.Channels.AddRange(channels);
            Plugin.Instance.SaveConfiguration();

            _logger.LogInformation("Generated {Count} automatic channels", channels.Count);

            return channels;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating automatic channels");
            return StatusCode(500, "Error generating channels");
        }
    }

    /// <summary>
    /// Gets available libraries for commercials and pre-rolls.
    /// </summary>
    [HttpGet("Libraries")]
    [Authorize(Policy = "DefaultAuthorization")]
    public ActionResult<List<LibraryInfo>> GetLibraries()
    {
        var libraries = _libraryManager.GetVirtualFolders()
            .Select(folder => new LibraryInfo
            {
                Id = folder.ItemId,
                Name = folder.Name,
                CollectionType = folder.CollectionType
            })
            .ToList();

        return libraries;
    }

    /// <summary>
    /// Gets items from a library.
    /// </summary>
    [HttpGet("Libraries/{libraryId}/Items")]
    [Authorize(Policy = "DefaultAuthorization")]
    public ActionResult<List<MediaItemInfo>> GetLibraryItems([FromRoute] string libraryId, [FromQuery] string? itemType = null)
    {
        var library = _libraryManager.GetItemById(Guid.Parse(libraryId));
        if (library == null)
            return NotFound();

        var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
        {
            Parent = library,
            Recursive = true
        };

        if (!string.IsNullOrEmpty(itemType))
        {
            if (Enum.TryParse<Jellyfin.Data.Enums.BaseItemKind>(itemType, true, out var kind))
            {
                query.IncludeItemTypes = new[] { kind };
            }
        }

        var items = _libraryManager.GetItemList(query)
            .Select(item => new MediaItemInfo
            {
                Id = item.Id.ToString(),
                Name = item.Name,
                Type = item.GetType().Name,
                Year = item.ProductionYear,
                Genres = item.Genres.ToList(),
                RunTimeTicks = item.RunTimeTicks
            })
            .ToList();

        return items;
    }

    /// <summary>
    /// Sets commercial libraries.
    /// </summary>
    [HttpPost("CommercialLibraries")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult SetCommercialLibraries([FromBody] List<string> libraryIds)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
            return BadRequest("Plugin not configured");

        config.CommercialLibraries = libraryIds;
        Plugin.Instance.SaveConfiguration();

        _logger.LogInformation("Updated commercial libraries");

        return NoContent();
    }

    /// <summary>
    /// Sets pre-roll libraries.
    /// </summary>
    [HttpPost("PreRollLibraries")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult SetPreRollLibraries([FromBody] List<string> libraryIds)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
            return BadRequest("Plugin not configured");

        config.PreRollLibraries = libraryIds;
        Plugin.Instance.SaveConfiguration();

        _logger.LogInformation("Updated pre-roll libraries");

        return NoContent();
    }

    /// <summary>
    /// Gets available genres.
    /// </summary>
    [HttpGet("Genres")]
    [Authorize(Policy = "DefaultAuthorization")]
    public ActionResult<List<string>> GetGenres()
    {
        var items = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
        {
            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie, Jellyfin.Data.Enums.BaseItemKind.Series },
            Recursive = true
        });

        var genres = items
            .SelectMany(item => item.Genres)
            .Distinct()
            .OrderBy(g => g)
            .ToList();

        return genres;
    }

    /// <summary>
    /// Gets available years.
    /// </summary>
    [HttpGet("Years")]
    [Authorize(Policy = "DefaultAuthorization")]
    public ActionResult<List<int>> GetYears()
    {
        var items = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
        {
            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie, Jellyfin.Data.Enums.BaseItemKind.Series },
            Recursive = true
        });

        var years = items
            .Where(item => item.ProductionYear.HasValue)
            .Select(item => item.ProductionYear.Value)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        return years;
    }
}

public class LibraryInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CollectionType { get; set; }
}

public class MediaItemInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? Year { get; set; }
    public List<string> Genres { get; set; } = new();
    public long? RunTimeTicks { get; set; }
}