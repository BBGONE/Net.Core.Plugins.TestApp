using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonUtils.Disposal;
using HostApp.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PluginContract;

namespace HostApp
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> logger;
        private readonly ILoggerFactory loggerFactory;
        private readonly IHostEnvironment environment;
        private readonly IPluginsManager<IPlugin> pluginsManager;

        public Worker(
            ILogger<Worker> logger,
            ILoggerFactory loggerFactory,
            IHostEnvironment environment,
            IPluginsManager<IPlugin> pluginsManager
            )
        {
            this.logger = logger;
            this.loggerFactory = loggerFactory;
            this.environment = environment;
            this.pluginsManager = pluginsManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            try
            {
                pluginsManager.Initialize(stoppingToken);

                {
                    var plugins = pluginsManager.CreateAllPlugins();
                    using (CompositeDisposable disposal = new CompositeDisposable(plugins))
                    {
                        Console.WriteLine(string.Join(", ", plugins.Select(p => $"{p.PluginName}").ToArray()));
                    }
                }

                byte[] data = new byte[32000];

                Random random = new Random();
                random.NextBytes(data);
                string inputData = Convert.ToBase64String(data);

                foreach (var num in Enumerable.Range(1, 200))
                {
                    var plugins = pluginsManager.CreateAllPlugins();
                    using (CompositeDisposable disposal = new CompositeDisposable(plugins))
                    {
                        Task<string>[] tasks = plugins.Select(r => r.HandleAsync(inputData)).ToArray();
                        await Task.WhenAll(tasks);
                        Console.WriteLine(num.ToString().PadLeft(2) + ". " + string.Join(',', tasks.Select(t => $"{t.GetAwaiter().GetResult()}").ToArray()));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: '{ex.GetType().Name}'");
            }

            Console.WriteLine("Press Ctrl+C to exit ...");

            // Console.WriteLine(environment.EnvironmentName);
            /*
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                
                await Task.Delay(3000, stoppingToken);
            }
            */
        }
    }
}
