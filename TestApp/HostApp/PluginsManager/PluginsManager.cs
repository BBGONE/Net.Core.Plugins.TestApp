using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using McMaster.NETCore.Plugins;
using McMaster.NETCore.Plugins.Internal;
using Microsoft.Extensions.Logging;
using PluginContract;

namespace HostApp.Plugins
{
    public enum PluginsManagerState : int
    {
        None = 0,
        Initialized = 1,
        Disposed = 2,
        Error = 3
    }

    public enum PluginState : int
    {
        None = 0,
        Initialized = 1,
        Disposed = 2,
        Error = 3
    }

    public class PluginsManager<TPlugin> : IPluginsManager<TPlugin>, IDisposable
        where TPlugin: class
    {
        private readonly ConcurrentDictionary<string, PluginInfo<TPlugin>> pluginsDictionary = new ConcurrentDictionary<string, PluginInfo<TPlugin>>();
        private volatile int managerState = 0;
        private CancellationToken stoppingToken;
        private MonitorPluginsTimer monitorPluginsTimer;
        private Debouncer debouncer;
        private readonly ILoggerFactory loggerFactory;
        private string PluginsDirectory { get; }

        public PluginsManager(string pluginsDirectory, ILoggerFactory loggerFactory)
        {
            this.PluginsDirectory = pluginsDirectory;
            this.stoppingToken = CancellationToken.None;
            this.loggerFactory = loggerFactory;
            this.monitorPluginsTimer = new MonitorPluginsTimer(TimeSpan.FromSeconds(10));
            this.monitorPluginsTimer.OnCheck += MonitorPluginsTimer_OnCheck;
            this.debouncer = new Debouncer(TimeSpan.FromSeconds(10));
        }

        private void MonitorPluginsTimer_OnCheck(object sender, EventArgs e)
        {
            if (managerState == (int)PluginsManagerState.Initialized)
            {
                AddOrRemovePlugins(false);
            }
        }

        private void StartMonitoring()
        {
            monitorPluginsTimer.Start();
        }

        private void AddOrRemovePlugins(bool isInitialization)
        {
            if (managerState != (int)PluginsManagerState.Initialized)
            {
                return;
            }

            foreach (var pluginInfo in this.pluginsDictionary.Values)
            {
                if (!Directory.Exists(Path.GetDirectoryName(pluginInfo.MainAssemblyPath)))
                {
                    this.RemovePlugin(pluginInfo);
                }
                else
                {
                    DateTime lastWriteTime = File.GetLastWriteTime(pluginInfo.MainAssemblyPath);
                    // Время на сборке изменилось (значит это другая версия - перезагружаем плагин)
                    if (pluginInfo.LastWriteTime != lastWriteTime)
                    {
                        pluginInfo.Reload();
                    }
                }
            }

            var pluginsLookup = this.pluginsDictionary.Values.ToLookup(p => p.DirectoryName);

            foreach (var dir in Directory.GetDirectories(this.PluginsDirectory))
            {
                var dirName = Path.GetFileName(dir);
                // если из этой директории плагины еще не загружались
                if (!pluginsLookup[dirName].Any())
                {
                    if (isInitialization)
                    {
                        LoadPlugins(dirName);
                    }
                    else
                    {
                        // Надо подождать немного, чтобы внутрь директории все файлы успели скопироваться (если идет процесс копирования)
                        debouncer.Execute(() =>
                        {
                            if (this.managerState != (int)PluginsManagerState.Initialized)
                            {
                                return;
                            }
                            LoadPlugins(dirName);
                        });
                    }
                }
            }
        }

        #region static members

        internal static IEnumerable<(string Name, Type PluginType)> GetPluginTypes(PluginLoader loader)
        {
            return GetPluginTypes(loader.LoadDefaultAssembly());
        }

        public static IEnumerable<(string Name, Type PluginType)> GetPluginTypes(Assembly assembly)
        {
            var pluginTypes = assembly
                             .GetTypes()
                             .Where(t => typeof(TPlugin).IsAssignableFrom(t) && !t.IsAbstract).ToList();

            var found = pluginTypes.Where(p => p.GetCustomAttributes(false).OfType<IPluginAttribute>().Any());

            var selected = found.Select(p => new { Name = p.GetCustomAttributes(false).OfType<IPluginAttribute>().Select(a => $"{a.PluginName}:{a.PluginVersion}").First(), PluginType = p }).ToArray();

            return selected.Select(f => (f.Name, f.PluginType)).ToArray();
        }
        #endregion

        private bool RemovePlugin(PluginInfo<TPlugin> pluginInfo)
        {
            bool result = pluginsDictionary.TryRemove(pluginInfo.Name, out var _);
            pluginInfo.Dispose();
            return result;
        }

        public void Initialize(CancellationToken stoppingToken)
        {
            if (Interlocked.CompareExchange(ref managerState, (int)PluginsManagerState.Initialized, (int)PluginsManagerState.None) != (int)PluginsManagerState.None)
            {
                throw new InvalidOperationException($"Нельзя инциализировать плагины, когда состояние {Enum.GetName(typeof(PluginsManagerState), (PluginsManagerState)this.managerState)}");
            }

            try
            {
                this.stoppingToken = stoppingToken;
                AddOrRemovePlugins(true);
                StartMonitoring();
                return;
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref managerState, (int)PluginsManagerState.Error);
                throw;
            }
        }

        private void PluginInfo_OnError(object sender, PluginErrorEventArgs<TPlugin> e)
        {
            // TODO: Add Logging here
            Console.WriteLine(e.Error.Message);
            this.RemovePlugin(e.PluginInfo);
        }

        public void Dispose()
        {
            var prevState = Interlocked.Exchange(ref managerState, (int)PluginsManagerState.Disposed);
            if (prevState == (int)PluginsManagerState.Disposed)
            {
                return;
            }

            if (monitorPluginsTimer != null)
            {
                monitorPluginsTimer.Stop();
                monitorPluginsTimer.OnCheck -= MonitorPluginsTimer_OnCheck;
                monitorPluginsTimer.Dispose();
                monitorPluginsTimer = null;
            }

            debouncer?.Dispose();
            debouncer = null;

            var arrPlugins = pluginsDictionary.Values.ToArray();
            pluginsDictionary.Clear();
            List<Exception> errors = new List<Exception>();
            foreach (var plugin in arrPlugins)
            {
                try
                {
                    plugin.Dispose();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            if (errors.Any())
            {
                throw new AggregateException(errors);
            }
        }

        public int LoadPlugins(string dirName)
        {
            string pluginDll = Path.Combine(this.PluginsDirectory, dirName, $"{dirName}.dll");

            if (!File.Exists(pluginDll))
            {
                throw new InvalidOperationException($"Не найдена сборка с плагином: {pluginDll}");
            }

            int cnt = 0;

            PluginLoader loader = PluginLoader.CreateFromAssemblyFile(
                    pluginDll,
                    true,
                    sharedTypes: new[] { typeof(TPlugin) },
                    (config) =>
                    {
                        config.EnableHotReload = true;
                    });

            if (!loader.IsUnloadable)
            {
                loader.Dispose();
                throw new InvalidOperationException($"Сборка {pluginDll} не выгружаемая и не может быть использована");
            }

            var plugins = GetPluginTypes(loader);

            foreach (var plugin in plugins)
            {
                var newPluginInfo = new PluginInfo<TPlugin>(loader, plugin, stoppingToken, this.loggerFactory);
                newPluginInfo.OnError += PluginInfo_OnError;
                this.OnPluginCreated?.Invoke(this, new PluginInfoCreatedEventArgs<TPlugin>(newPluginInfo));

                var pluginInfo = pluginsDictionary.AddOrUpdate(plugin.Name, newPluginInfo, (key, oldPluginInfo) =>
                {
                    oldPluginInfo.Dispose();
                    return newPluginInfo;
                });

                ++cnt;
            }

            return cnt;
        }

        public event EventHandler<PluginInfoCreatedEventArgs<TPlugin>> OnPluginCreated;

        public TPlugin CreatePlugin(string name)
        {
            if (!pluginsDictionary.TryGetValue(name, out var pluginInfo))
            {
                throw new InvalidOperationException($"Не найден плагин: {name}");
            }

            return pluginInfo.CreatePluginInstance();
        }

        public TPlugin[] CreateAllPlugins()
        {
            return pluginsDictionary.Values.Select(p => p.CreatePluginInstance()).ToArray();
        }

        public int Count
        {
            get
            {
                return pluginsDictionary.Count;
            }
        }
    }
}
