using System;
using System.Threading;

namespace CommonUtils.Disposal
{
    public sealed class AnonymousDisposable : IDisposable
    {
        private readonly Action _action;
        private int _disposed;

        public AnonymousDisposable(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _action();
            }
        }
    }
}
