using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Extensions.Logging.RollingFile.Internal
{
    public abstract class BatchingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogMessage> _currentBatch = new List<LogMessage>();
        private readonly TimeSpan _interval;
        private readonly int? _queueCapacity;
        private readonly int _batchSize;
        private int _messagesDropped;
        private volatile int _queueLength;

        private BlockingCollection<LogMessage> _messageQueue;
        private Task _outputTask;
        private CancellationTokenSource _cancellationTokenSource;

        protected BatchingLoggerProvider(IOptions<BatchingLoggerOptions> options)
        {
            var loggerOptions = options.Value;
            if (loggerOptions.BatchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(loggerOptions.BatchSize), $"{nameof(loggerOptions.BatchSize)} must be a positive number.");
            }
            if (loggerOptions.FlushPeriod <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(loggerOptions.FlushPeriod), $"{nameof(loggerOptions.FlushPeriod)} must be longer than zero.");
            }

            _interval = loggerOptions.FlushPeriod;
            _batchSize = loggerOptions.BatchSize ?? 32;
            _queueCapacity = loggerOptions.BackgroundQueueSize;

            Start();
        }

        protected abstract void WriteMessages(IEnumerable<LogMessage> messages, CancellationToken token);

        private void ProcessLogQueue(object state)
        {
            bool isExit = _cancellationTokenSource.IsCancellationRequested;
            while (!isExit)
            {
                var limit = _batchSize;

                while (limit > 0 && _messageQueue.TryTake(out var message))
                {
                    Interlocked.Decrement(ref _queueLength);
                    _currentBatch.Add(message);
                    limit--;
                }

                this.WriteBatch();

                isExit = _cancellationTokenSource.IsCancellationRequested;
                if (!isExit && _queueLength == 0)
                {
                    Interval(_interval, _cancellationTokenSource.Token);
                }
            }
        }

        private void WriteBatch()
        {
            var messagesDropped = Interlocked.Exchange(ref _messagesDropped, 0);
            if (messagesDropped != 0)
            {
                _currentBatch.Add(new LogMessage()
                {
                    Timestamp = DateTimeOffset.Now,
                    Category = nameof(BatchingLoggerProvider),
                    Message = $@"{messagesDropped} message(s) dropped because of queue size limit. 
Increase the queue size or decrease logging verbosity to avoid this.",
                    LogLevel = LogLevel.Error
                });
            }

            if (_currentBatch.Count > 0)
            {
                try
                {
                    WriteMessages(_currentBatch, _cancellationTokenSource.Token);
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    _currentBatch.Clear();
                }
            }
        }

        protected virtual void Interval(TimeSpan interval, CancellationToken cancellationToken)
        {
            try
            {
                Task.Delay(interval, cancellationToken).Wait();
            }
            catch
            {
                // ignore
            }
        }

        internal void AddMessage(string category, DateTimeOffset timestamp, string message, LogLevel logLevel, EventId eventId)
        {
            if (!_messageQueue.IsAddingCompleted)
            {
                try
                {
                    var msg = new LogMessage { Category= category, Message = message, Timestamp = timestamp, LogLevel = logLevel, EventId = eventId };
                    if (!_messageQueue.TryAdd(msg, millisecondsTimeout: 0, cancellationToken: _cancellationTokenSource.Token))
                    {
                        Interlocked.Increment(ref _messagesDropped);
                    }
                    else
                    {
                        Interlocked.Increment(ref _queueLength);
                    }
                }
                catch
                {
                    //cancellation token canceled or CompleteAdding called
                }
            }
        }

        private void Start()
        {
            _messageQueue = _queueCapacity == null ?
                new BlockingCollection<LogMessage>(new ConcurrentQueue<LogMessage>()) :
                new BlockingCollection<LogMessage>(new ConcurrentQueue<LogMessage>(), _queueCapacity.Value);

            _cancellationTokenSource = new CancellationTokenSource();
            _outputTask = Task.Factory.StartNew(
                ProcessLogQueue,
                _cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning);
        }

        private void Stop()
        {
            _cancellationTokenSource.Cancel();
            _messageQueue.CompleteAdding();

            try
            {
                _outputTask.Wait(_interval);
            }
            catch (TaskCanceledException)
            {
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1 && ex.InnerExceptions[0] is TaskCanceledException)
            {
            }
        }

        public void Dispose()
        {
            Stop();
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new BatchingLogger(this, categoryName);
        }
    }
}
