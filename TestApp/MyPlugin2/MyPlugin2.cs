using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TransformBlockTest;
using PluginContract;

namespace MyPlugin
{
    [PluginAttribute("MyPlugin", "V2")]
    internal class MyPlugin2 : IPlugin
    {
        private readonly CancellationToken externalCancellationToken;
        private CancellationTokenSource cts;
        private CancellationTokenSource linkedCts;

        private readonly string pluginName;
        private readonly IPluginLoggerFactory loggerFactory;
        private IPluginLogger logger;

        public MyPlugin2(string pluginName, CancellationToken cancellationToken, IPluginLoggerFactory loggerFactory)
        {
            this.pluginName = pluginName;
            this.externalCancellationToken = cancellationToken;
            this.cts = new CancellationTokenSource();
            this.linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.cts.Token, this.externalCancellationToken);
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger(nameof(MyPlugin2));
        }

        public Task Configure(string configuration, string configVersion)
        {
            // Можно десериализовать конфигурацию и закэшировать ее в статическом поле
            // обновлять кэш только тогда когда меняется configVersion (незабыть про locking при доступе к кэшу)
            return Task.CompletedTask;
        }

        public async Task<string> HandleAsync(string input)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            try
            {
                await Task.Yield();
                byte[] data = Convert.FromBase64String(input);
                Executor executor = new Executor(this.loggerFactory);
                await executor.DoWork(data);
            }
            finally
            {
                sw.Stop();
            }

            string msg = $"plugin2: {sw.ElapsedMilliseconds}ms, Now: {DateTime.Now:HH:mm:ss.ff}"; ;
            logger?.Log(LogLevel.Information, 0, msg);
            return msg;
        }

        public void Dispose()
        {
            this.Cancel();
            this.logger = null;
        }

        public void Cancel()
        {
            cts?.Cancel();
        }

        public CancellationToken CancellationToken => linkedCts.Token;

        public string PluginName => pluginName;
    }
}
