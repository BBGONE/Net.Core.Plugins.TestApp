using Microsoft.Extensions.Logging;
using System;
using System.Xml.Linq;

namespace Extensions.Logging.RollingFile.Internal
{
    public class BatchingLogger : ILogger
    {
        private readonly BatchingLoggerProvider _provider;
        private readonly string _category;

        public BatchingLogger(BatchingLoggerProvider loggerProvider, string categoryName)
        {
            _provider = loggerProvider;
            _category = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
            {
                return false;
            }
            return true;
        }

        public void Log<TState>(DateTimeOffset timestamp, LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string stateString = formatter(state, exception);
            string error = exception?.ToFullMessageString();
            string extendedMessage = string.Empty;
            if (exception?.InnerException != null)
            {
                extendedMessage = exception.InnerException.ToFullMessageString();
            }

            // we can not use formatter
            XElement xel = new XElement("message",
                                        new XAttribute("time", timestamp.UtcDateTime),
                                        new XAttribute("logLevel", logLevel),
                                        new XAttribute("eventId", eventId),
                                        new XElement("category", _category),
                                        string.IsNullOrEmpty(stateString)? null: new XElement("state", stateString),
                                        string.IsNullOrEmpty(error)? null:  new XElement("error", error),
                                        string.IsNullOrEmpty(extendedMessage) ? null : new XElement("extended", extendedMessage)
                                     );

            string msg = xel.ToString(SaveOptions.None);

            _provider.AddMessage(_category, timestamp, msg, logLevel, eventId);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Log(DateTimeOffset.Now, logLevel, eventId, state, exception, formatter);
        }
    }
}
