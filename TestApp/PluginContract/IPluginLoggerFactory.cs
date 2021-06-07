using System;

namespace PluginContract
{
    public interface IPluginLoggerFactory
    {
        IPluginLogger CreateLogger(string categoryName);
    }
}
