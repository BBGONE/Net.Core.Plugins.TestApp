using System;

namespace HostApp.Plugins
{
    public class PluginErrorEventArgs<TPlugin>: EventArgs
        where TPlugin: class
    {
        private readonly PluginInfo<TPlugin> pluginInfo;
        private Exception error;
        public PluginErrorEventArgs(PluginInfo<TPlugin> pluginInfo, Exception exception)
        {
            this.pluginInfo = pluginInfo;
            this.error = exception;
        }

        public PluginInfo<TPlugin> PluginInfo => this.pluginInfo;

        public Exception Error => this.error;
    }
}
