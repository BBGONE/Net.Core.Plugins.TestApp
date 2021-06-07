using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PluginContract;

namespace CommonUtils.TPLBlocks
{
    public abstract class BaseTransformBlock<TInput, TOutput> : IWorkLoad<TInput>, ITransformBlock<TInput, TOutput>
    {
        private Object _SyncLock = new Object();
        private ICallbackProxy<TInput> _callbackProxy;
        private bool _isDisposed = false;
        private readonly IPluginLoggerFactory _loggerFactory;
        private readonly TCallBack<TInput> _callBack;
        private Func<TInput, Task<TOutput>> _body;
        private Func<TOutput, Task>[] _outputSinks;
        private Guid _id = new Guid();
        private CancellationToken? _externalCancellationToken;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _linkedCts;

        public BaseTransformBlock(Func<TInput, Task<TOutput>> body, IPluginLoggerFactory loggerFactory, CancellationToken? cancellationToken= null)
        {
            _body = body;
            _outputSinks = null;
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _externalCancellationToken = cancellationToken;
            _cts = new CancellationTokenSource();
            
            if (_externalCancellationToken != null)
                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, _externalCancellationToken.Value);
            else
                _linkedCts = _cts;

            _callBack = new TCallBack<TInput>(_loggerFactory.CreateLogger(this.GetType().Name));
            _callBack.ResultAsync.ContinueWith((antecedent) => {
                try
                {
                    _cts?.Cancel();
                    this.OnCompetion();
                }
                finally
                {
                    // internal cleanup
                    this._OnCompetion();
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
            _callbackProxy = null;
        }

        private void  _OnCompetion()
        {
            // the output sink won't be needed after completion (so cleat it at once)
            _outputSinks = null;
        }

        /// <summary>
        /// Used for stopping all threads in decendants
        /// </summary>
        protected abstract void OnCompetion();
     

        public abstract ValueTask<bool> Post(TInput msg);
        
        protected virtual CancellationToken GetCancellationToken()
        {
            return _linkedCts.Token;
        }

        protected long UpdateBatchSize(long addValue, bool isComplete)
        {
            return this._callBack.UpdateBatchSize(addValue, isComplete);
        }

        protected ICallbackProxy<TInput> CallbackProxy
        {
            get
            {
                if (!_isDisposed)
                {
                    if (_callbackProxy == null)
                    {
                        LazyInitializer.EnsureInitialized(ref _callbackProxy, ref _SyncLock, () => new CallbackProxy<TInput>(_callBack, _loggerFactory, this.GetCancellationToken()));
                    }
                }
                else
                {
                    throw new ObjectDisposedException(this.GetType().Name);
                }

                return _callbackProxy;
            }
        }

        public Task Completion
        {
            get
            {
                return _callBack.ResultAsync;
            }
        }

        public event Func<TOutput, Task> OutputSink
        {
            add {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(this.GetType().Name);
                }

                lock (this._SyncLock)
                {
                    var oldSinks = _outputSinks;
                    
                    if (oldSinks == null)
                    {
                        _outputSinks = new[] { value };
                    }
                    else
                    {
                        int len = oldSinks.Length;
                        var sinks = new Func<TOutput, Task>[len + 1];
                        Array.Copy(oldSinks, sinks, len);
                        sinks[len] = value;
                        _outputSinks = sinks;
                    }
                }
               
            }
            remove
            {
                lock (this._SyncLock)
                {
                    var oldSinks = _outputSinks;
                    if (oldSinks == null)
                    {
                        return;
                    }
                    else
                    {
                        _outputSinks = oldSinks.Where((val) => val != value).ToArray();
                    }
                }
            }
        }

        public BatchInfo BatchInfo { get => _callBack.BatchInfo; }
        public Guid Id { get => _id; set => _id = value; }
        public bool IsCompleted { get => _callBack.ResultAsync.IsCompleted; }

        public long Complete(Exception exception = null)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }

            if (exception == null)
            {
                return this.UpdateBatchSize(0, true);
            }
            else
            {
                if (exception is AggregateException aggex)
                {
                    Exception firstError = null;

                    aggex.Flatten().Handle((err) => {
                        if (err is OperationCanceledException)
                        {
                            return true;
                        }
                        firstError = firstError ?? err;
                        return true;
                    });

                    if (firstError != null)
                    {
                        CallbackProxy.JobCompleted(firstError);
                    }
                    else
                    {
                        CallbackProxy.JobCancelled();
                    }
                }
                else if (exception is OperationCanceledException)
                {
                    CallbackProxy.JobCancelled();
                }
                else
                {
                    CallbackProxy.JobCompleted(exception);
                }

                return this.UpdateBatchSize(0, true);
            }
        }

        protected virtual void OnDispose()
        {
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                this.OnDispose();

                this._OnCompetion();

                var oldCallbackProxy = Interlocked.Exchange(ref _callbackProxy, null);
                if (oldCallbackProxy != null)
                {
                    (oldCallbackProxy as IDisposable)?.Dispose();
                }

                _linkedCts?.Dispose();
                if (_linkedCts != _cts && _cts != null)
                {
                    _cts?.Dispose();
                }
                _linkedCts = null;
                _cts = null;

                _isDisposed = true;
            }
        }

        async Task<bool> IWorkLoad<TInput>.DispatchMessage(TInput message, long taskId, CancellationToken token)
        {
            try
            {
                var output = await _body(message);

                var sinks = _outputSinks;

                if (sinks != null)
                {
                    for(int i=0; i< sinks.Length;++i)
                    {
                        await sinks[i](output);
                    }
                }

                await CallbackProxy.TaskCompleted(message, null);
            }
            catch (Exception ex)
            {
                await CallbackProxy.TaskCompleted(message, ex);
            }
            return false;
        }
    }
}
