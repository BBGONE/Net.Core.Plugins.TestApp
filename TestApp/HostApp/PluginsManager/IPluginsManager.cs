using System;
using System.Threading;

namespace HostApp.Plugins
{
    public interface IPluginsManager<TPlugin>
        where TPlugin: class
    {
        int Count { get; }

        event EventHandler<PluginInfoCreatedEventArgs<TPlugin>> OnPluginCreated;

        TPlugin[] CreateAllPlugins();
        TPlugin CreatePlugin(string name);
        void Dispose();
        void Initialize(CancellationToken stoppingToken);
        int LoadPlugins(string dirName);
    }
}
