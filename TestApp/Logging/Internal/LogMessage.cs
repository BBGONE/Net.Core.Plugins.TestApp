using Microsoft.Extensions.Logging;
using System;

namespace Extensions.Logging.RollingFile.Internal
{
    public struct LogMessage
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Message { get; set; }
        public LogLevel LogLevel { get; set; }
        public EventId EventId { get; set; }
        public string Category { get; set; }
    }
}
