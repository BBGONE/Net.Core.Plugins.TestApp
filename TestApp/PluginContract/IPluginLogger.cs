using System;

namespace PluginContract
{
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }

    public readonly struct EventId
    {
        public EventId(int id, string name = null)
        {
            this.Id = id;
            this.Name = name;
        }

        public int Id { get; }
        public string Name { get; }
    }

    public struct LogMessage
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Message { get; set; }
        public LogLevel LogLevel { get; set; }
        public int EventId { get; set; }
    }

    public interface IPluginLogger
    {
        void Log(LogLevel logLevel, int eventId, string message);
    }
}
