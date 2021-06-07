using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using PluginContract;

namespace HostApp.Logging
{
    public class PluginLoggerFactory : IPluginLoggerFactory
    {
        private readonly ILoggerFactory loggerFactory;

        public PluginLoggerFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
        }

        public IPluginLogger CreateLogger(string categoryName)
        {
            ILogger logger = this.loggerFactory.CreateLogger(categoryName);

            return new PluginLogger((logMessage) =>
            {
                logger.Log((Microsoft.Extensions.Logging.LogLevel)logMessage.LogLevel,
                    logMessage.EventId,
                    logMessage,
                    null,
                    (msg, ex) => msg.Message);
            });
        }
    }
}
