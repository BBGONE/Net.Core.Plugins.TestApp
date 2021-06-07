using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HostApp.Logging;
using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Logging;

namespace HostApp.Plugins
{
    public class PluginInfo<T> : IDisposable
             where T : class
    {
        private volatile int pluginInfoState = 0;
        private Type pluginType;
        private readonly CancellationToken cancellationToken;
        private readonly string name;
        private readonly string directoryName;
        private DateTime lastWriteTime;
        private ILoggerFactory loggerFactory;

        public PluginInfo(PluginLoader loader, (string Name, System.Type PluginType) plugin, CancellationToken cancellationToken, ILoggerFactory loggerFactory)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                this.loggerFactory = loggerFactory;
                this.name = plugin.Name;
                this.pluginType = plugin.PluginType;
                this.directoryName = Path.GetFileName(Path.GetDirectoryName(loader.MainAssemblyPath));

                this.Loader = loader;
                this.Loader.Reloaded += Loader_Reloaded;
                this.lastWriteTime = File.GetLastWriteTime(loader.MainAssemblyPath);
                this.pluginInfoState = (int)PluginState.Initialized;
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref this.pluginInfoState, (int)PluginState.Error);
                throw;
            }
        }

        private void Loader_Reloaded(object sender, PluginReloadedEventArgs eventArgs)
        {
            if (this.pluginInfoState != (int)PluginState.Initialized)
            {
                return;
            }

            try
            {
                var pluginTypes = PluginsManager<T>.GetPluginTypes(this.Loader);
                if (!pluginTypes.Any())
                {
                    throw new InvalidOperationException($"Сборка {this.Loader.MainAssemblyPath} не содержит плагинов");
                }

                var found = pluginTypes.SingleOrDefault(p => p.Name == this.name);

                if (string.IsNullOrEmpty(found.Name))
                {
                    throw new InvalidOperationException($"Сборка {this.Loader.MainAssemblyPath} не содержит плагинов: {this.name}");
                }

                this.PluginType = found.PluginType;
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref pluginInfoState, (int)PluginState.Error);
                OnError?.Invoke(this, new PluginErrorEventArgs<T>(this, ex));
            }
        }

        public void Dispose()
        {
            var prevState = Interlocked.Exchange(ref pluginInfoState, (int)PluginState.Disposed);
            if (prevState == (int)PluginState.Disposed)
            {
                return;
            }

            this.PluginType = null;
            var loader = this.Loader;
            this.Loader = null;
            this.OnError = null;
            if (loader != null)
            {
                loader.Reloaded -= Loader_Reloaded;
                loader.Dispose();
            }
        }

        public void Reload()
        {
            if (this.pluginInfoState != (int)PluginState.Initialized)
            {
                return;
            }

            this.lastWriteTime = File.GetLastWriteTime(this.MainAssemblyPath);
            this.Loader.Reload();
        }

        public virtual T CreatePluginInstance()
        {
            return (T)Activator.CreateInstance(pluginType, this.Name, cancellationToken, new PluginLoggerFactory(this.loggerFactory));
        }

        public PluginLoader Loader { get; private set; }

        public string MainAssemblyPath => Loader?.MainAssemblyPath ?? String.Empty;

        public Type PluginType
        {
            get
            {
                return pluginType;
            }
            private set
            {
                Type initialValue;
                do
                {
                    initialValue = this.pluginType;
                } while (initialValue != Interlocked.CompareExchange(ref pluginType, value, initialValue));
            }
        }

        public PluginState State => (PluginState)pluginInfoState;

        /// <summary>
        /// Имя плагина (включает его версию)
        /// </summary>
        public string Name => name;

        /// <summary>
        /// Имя дирекотории в которой находится сборка содержащая плагин
        /// </summary>
        public string DirectoryName => directoryName;

        /// <summary>
        /// Запомненное время когдда последний раз изменялась сборка содержащая плагин
        /// </summary>
        public DateTime LastWriteTime => lastWriteTime;

        public event EventHandler<PluginErrorEventArgs<T>> OnError;
    }
}
