using System;

namespace HostApp.Plugins
{
    public class PluginInfoCreatedEventArgs<TPlugin>: EventArgs
        where TPlugin: class
    {
        private readonly PluginInfo<TPlugin> pluginInfo;
        public PluginInfoCreatedEventArgs(PluginInfo<TPlugin> pluginInfo)
        {
            this.pluginInfo = pluginInfo;
        }

        public PluginInfo<TPlugin> PluginInfo => this.pluginInfo;
    }
}
