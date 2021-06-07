using System;
using System.IO;
using System.Threading.Tasks;
using HostApp.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PluginContract;

namespace HostApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                await CreateHostBuilder(args).Build().RunAsync();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());
                })
                .ConfigureLogging((context, logBuilder) => {
                    logBuilder.ClearProviders();
                    logBuilder.AddFile(opts =>
                    {
                         context.Configuration.GetSection("FileLoggingOptions").Bind(opts);
                    });

                })
                .ConfigureServices((hostContext, services) =>
                {
                    
                    services.AddSingleton<IPluginsManager<IPlugin>>((sp)=> {
                        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
                        var pluginsManager = new PluginsManager<IPlugin>(pluginsDir, sp.GetRequiredService<ILoggerFactory>());
                        return pluginsManager;
                    });

                    services.AddHostedService<Worker>();
                });

    }
}
