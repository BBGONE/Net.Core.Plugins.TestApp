using System;

namespace HostApp.Plugins
{
    public class MonitorPluginsTimer: IDisposable
    {
        private System.Threading.Timer timer;
        private readonly TimeSpan timerTime;

        public MonitorPluginsTimer(TimeSpan timerTime)
        {
            this.timerTime = timerTime;

            this.timer = new System.Threading.Timer((state) => {
                try
                {
                    this.Stop();
                    this.OnCheck(this, EventArgs.Empty);
                }
                finally
                {
                    this.Resume();
                }
            });
        }

        public void Start()
        {
            if (!disposedValue)
            {
                timer.Change((int)TimeSpan.FromSeconds(5).TotalMilliseconds, (int)timerTime.TotalMilliseconds);
            }
        }

        public void Stop()
        {
            if (!disposedValue)
            {
                timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
        }


        public void Resume()
        {
            if (!disposedValue)
            {
                timer.Change((int)timerTime.TotalMilliseconds, (int)timerTime.TotalMilliseconds);
            }
        }

        public event EventHandler OnCheck;

        #region IDisposable Support
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this.timer.Dispose();
                }

                this.timer = null;

                disposedValue = true;
            }
        }
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
