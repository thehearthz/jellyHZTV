using Jellyfin.Plugin.VirtualChannels.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.VirtualChannels;

/// <summary>
/// Register virtual channel services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ChannelManager>();
        serviceCollection.AddSingleton<StreamingService>();
        serviceCollection.AddSingleton<VirtualChannelProvider>();
    }
}