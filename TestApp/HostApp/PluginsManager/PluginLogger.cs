using System;
using System.Collections.Generic;
using System.Text;
using PluginContract;

namespace HostApp.Logging
{
    public class PluginLogger : IPluginLogger
    {
        private Action<LogMessage> onLog;
        public PluginLogger(Action<LogMessage> onLog)
        {
            this.onLog = onLog;
        }

        public void Log(LogLevel logLevel, int eventId, string message)
        {
            onLog?.Invoke(new LogMessage { Timestamp = DateTime.Now, LogLevel = logLevel, EventId = eventId, Message = message });
        }
    }
}
