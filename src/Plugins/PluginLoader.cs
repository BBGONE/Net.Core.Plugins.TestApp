﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using McMaster.NETCore.Plugins.Internal;
using McMaster.NETCore.Plugins.Loader;

namespace McMaster.NETCore.Plugins
{
    /// <summary>
    /// This loader attempts to load binaries for execution (both managed assemblies and native libraries)
    /// in the same way that .NET Core would if they were originally part of the .NET Core application.
    /// <para>
    /// This loader reads configuration files produced by .NET Core (.deps.json and runtimeconfig.json)
    /// as well as a custom file (*.config files). These files describe a list of .dlls and a set of dependencies.
    /// The loader searches the plugin path, as well as any additionally specified paths, for binaries
    /// which satisfy the plugin's requirements.
    /// </para>
    /// </summary>
    public class PluginLoader : IDisposable
    {
#if FEATURE_UNLOAD
        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="isUnloadable">Enable unloading the plugin from memory.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, bool isUnloadable, Type[] sharedTypes)
            => CreateFromAssemblyFile(assemblyFile, isUnloadable, sharedTypes, _ => { });

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="isUnloadable">Enable unloading the plugin from memory.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <param name="configure">A function which can be used to configure advanced options for the plugin loader.</param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, bool isUnloadable, Type[] sharedTypes, Action<PluginConfig> configure)
        {
            return CreateFromAssemblyFile(assemblyFile,
                    sharedTypes,
                    config =>
                    {
                        config.IsUnloadable = isUnloadable;
                        configure(config);
                    });
        }
#endif

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, Type[] sharedTypes)
            => CreateFromAssemblyFile(assemblyFile, sharedTypes, _ => { });

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <param name="configure">A function which can be used to configure advanced options for the plugin loader.</param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, Type[] sharedTypes, Action<PluginConfig> configure)
        {
            return CreateFromAssemblyFile(assemblyFile,
                    config =>
                    {
                        if (sharedTypes != null)
                        {
                            var uniqueAssemblies = new HashSet<Assembly>();
                            foreach (var type in sharedTypes)
                            {
                                uniqueAssemblies.Add(type.Assembly);
                            }

                            foreach (var assembly in uniqueAssemblies)
                            {
                                config.SharedAssemblies.Add(assembly.GetName());
                            }
                        }
                        configure(config);
                    });
        }

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile)
            => CreateFromAssemblyFile(assemblyFile, _ => { });

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="configure">A function which can be used to configure advanced options for the plugin loader.</param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, Action<PluginConfig> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var config = new PluginConfig(assemblyFile);
            configure(config);
            return new PluginLoader(config);
        }

        private readonly PluginConfig _config;
        private ManagedLoadContext _context;
        private readonly AssemblyLoadContextBuilder _contextBuilder;
        private volatile int _disposed;

        /// <summary>
        /// Initialize an instance of <see cref="PluginLoader" />
        /// </summary>
        /// <param name="config">The configuration for the plugin.</param>
        public PluginLoader(PluginConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _contextBuilder = CreateLoadContextBuilder(config);
            _context = (ManagedLoadContext)_contextBuilder.Build();
        }

        /// <summary>
        /// Path to the assembly which contain the Plugin
        /// </summary>
        public string MainAssemblyPath
        {
            get
            {
                return _config.MainAssemblyPath;
            }
        }

        /// <summary>
        /// True when this plugin is capable of being unloaded.
        /// </summary>
        public bool IsUnloadable
        {
            get
            {
#if FEATURE_UNLOAD
                return _context.IsCollectible;
#else
                return false;
#endif
            }
        }

#if FEATURE_UNLOAD

        /// <summary>
        /// This event is raised when the plugin has been reloaded.
        /// If <see cref="PluginConfig.EnableHotReload" /> was set to <c>true</c>,
        /// the plugin will be reloaded when files on disk are changed.
        /// </summary>

        public event PluginReloadedEventHandler? Reloaded;


        /// <summary>
        /// The unloads and reloads the plugin assemblies.
        /// This method throws if <see cref="IsUnloadable" /> is <c>false</c>.
        /// </summary>
        public void Reload()
        {
            EnsureNotDisposed();

            if (!IsUnloadable)
            {
                throw new InvalidOperationException("Reload cannot be used because IsUnloadable is false");
            }

            _context.Unload();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            _context = (ManagedLoadContext)_contextBuilder.Build();
            Reloaded?.Invoke(this, new PluginReloadedEventArgs(this));
        }
#endif

        internal AssemblyLoadContext LoadContext => _context;

        /// <summary>
        /// Load the main assembly for the plugin.
        /// </summary>
        public Assembly LoadDefaultAssembly()
        {
            EnsureNotDisposed();
            return _context.LoadAssemblyFromFilePath(_config.MainAssemblyPath);
        }

        /// <summary>
        /// Load an assembly by name.
        /// </summary>
        /// <param name="assemblyName">The assembly name.</param>
        /// <returns>The assembly.</returns>
        public Assembly LoadAssembly(AssemblyName assemblyName)
        {
            EnsureNotDisposed();
            return _context.LoadFromAssemblyName(assemblyName);
        }

        /// <summary>
        /// Load an assembly from path.
        /// </summary>
        /// <param name="assemblyPath">The assembly path.</param>
        /// <returns>The assembly.</returns>
        public Assembly LoadAssemblyFromPath(string assemblyPath)
            => _context.LoadAssemblyFromFilePath(assemblyPath);

        /// <summary>
        /// Load an assembly by name.
        /// </summary>
        /// <param name="assemblyName">The assembly name.</param>
        /// <returns>The assembly.</returns>
        public Assembly LoadAssembly(string assemblyName)
        {
            EnsureNotDisposed();
            return LoadAssembly(new AssemblyName(assemblyName));
        }

#if !NETCOREAPP2_1
        /// <summary>
        /// Sets the scope used by some System.Reflection APIs which might trigger assembly loading.
        /// <para>
        /// See https://github.com/dotnet/coreclr/blob/v3.0.0/Documentation/design-docs/AssemblyLoadContext.ContextualReflection.md for more details.
        /// </para>
        /// </summary>
        /// <returns></returns>
        public AssemblyLoadContext.ContextualReflectionScope EnterContextualReflection()
            => _context.EnterContextualReflection();
#endif

        /// <summary>
        /// Disposes the plugin loader. This only does something if <see cref="IsUnloadable" /> is true.
        /// When true, this will unload assemblies which which were loaded during the lifetime of the plugin.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            {
                return;
            }

#if FEATURE_UNLOAD
            if (_context.IsCollectible)
            {
                _context.Unload();
            }
#endif
        }

        private void EnsureNotDisposed()
        {
            if (_disposed == 1)
            {
                throw new ObjectDisposedException(nameof(PluginLoader));
            }
        }

        private static AssemblyLoadContextBuilder CreateLoadContextBuilder(PluginConfig config)
        {
            var builder = new AssemblyLoadContextBuilder();

            builder.SetMainAssemblyPath(config.MainAssemblyPath);
            builder.SetDefaultContext(config.DefaultContext);

            foreach (var ext in config.PrivateAssemblies)
            {
                builder.PreferLoadContextAssembly(ext);
            }

            if (config.PreferSharedTypes)
            {
                builder.PreferDefaultLoadContext(true);
            }

#if FEATURE_UNLOAD
            if (config.IsUnloadable || config.EnableHotReload)
            {
                builder.EnableUnloading();
            }

            if (config.LoadInMemory)
            {
                builder.PreloadAssembliesIntoMemory();
                builder.ShadowCopyNativeLibraries();
            }
#endif

            builder.IsLazyLoaded(config.IsLazyLoaded);
            foreach (var assemblyName in config.SharedAssemblies)
            {
                builder.PreferDefaultLoadContextAssembly(assemblyName);
            }

#if !FEATURE_NATIVE_RESOLVER

            // In .NET Core 3.0, this code is unnecessary because the API, AssemblyDependencyResolver, handles parsing these files.
            var baseDir = Path.GetDirectoryName(config.MainAssemblyPath);
            var assemblyFileName = Path.GetFileNameWithoutExtension(config.MainAssemblyPath);

            var depsJsonFile = Path.Combine(baseDir, assemblyFileName + ".deps.json");
            if (File.Exists(depsJsonFile))
            {
                builder.AddDependencyContext(depsJsonFile);
            }

            var pluginRuntimeConfigFile = Path.Combine(baseDir, assemblyFileName + ".runtimeconfig.json");

            builder.TryAddAdditionalProbingPathFromRuntimeConfig(pluginRuntimeConfigFile, includeDevConfig: true, out _);

            // Always include runtimeconfig.json from the host app.
            // in some cases, like `dotnet test`, the entry assembly does not actually match with the
            // runtime config file which is why we search for all files matching this extensions.
            foreach (var runtimeconfig in Directory.GetFiles(AppContext.BaseDirectory, "*.runtimeconfig.json"))
            {
                builder.TryAddAdditionalProbingPathFromRuntimeConfig(runtimeconfig, includeDevConfig: true, out _);
            }
#endif

            return builder;
        }
    }
}