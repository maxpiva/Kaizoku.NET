using extension.bridge;
using ikvm.runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.Runtime.Loader;

namespace Mihon.ExtensionsBridge.Core.Runtime
{
    public class BridgeHost : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IWorkingFolderStructure _folder;
        private readonly IBridgeManager _manager;
        private readonly ILoggerFactory _loggerFactory;
        private ILogger _androidLogger;
        private IkvmAssemblyLoadContext alc;

        public sealed class IkvmAssemblyLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;

            public IkvmAssemblyLoadContext(string baseDirectory, bool isCollectible = false)
                : base(nameof(IkvmAssemblyLoadContext), isCollectible)
            {
                _resolver = new AssemblyDependencyResolver(baseDirectory);
                Resolving += OnResolving; // avoid re-entrancy into arbitrary code
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                // Deterministic, non-blocking resolution only.
                var path = _resolver.ResolveAssemblyToPath(assemblyName);
                return path is null ? null : LoadFromAssemblyPath(path);
            }

            private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
            {
                // No I/O beyond direct path probe, no waits/awaits, no calls into IKVM.
                var path = _resolver.ResolveAssemblyToPath(name);
                return path is null ? null : LoadFromAssemblyPath(path);
            }
        }


        public BridgeHost(ILogger<BridgeHost> logger, IWorkingFolderStructure folder, IBridgeManager manager, ILoggerFactory loggerFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }
        public 
        static void Preload(IkvmAssemblyLoadContext alc, string simpleName)
        {
            var asmPath = Path.Combine(AppContext.BaseDirectory, $"{simpleName}.dll");
            if (File.Exists(asmPath))
            {
                alc.LoadFromAssemblyPath(asmPath);
            }
        }
        private async Task InitAndroidAppAsync(IWorkingFolderStructure folder, ILogger logger, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Android App initializing...");


            Mihon.ExtensionsBridge.Models.Preferences? prefs = await _folder.LoadPreferencesAsync(cancellationToken);
            if (prefs == null)
            {
                prefs = new Mihon.ExtensionsBridge.Models.Preferences();
            }
            var baseDir = Path.GetFullPath(AppContext.BaseDirectory);
            Startup.addBootClassPathAssembly(Assembly.LoadFrom(Path.Combine(baseDir, "Android.Compat.dll")));
            /*

            // Create and pre-warm an isolated, non-reentrant ALC for IKVM
            var baseDir = Path.GetFullPath(AppContext.BaseDirectory);
            alc = new IkvmAssemblyLoadContext(baseDir);

            // Eager-load IKVM bits and dependent Java/Android bridge assemblies before using them.
            // This prevents on-demand resolution from happening under loader locks.
            Preload(alc, "IKVM.Java");
            Preload(alc, "IKVM.Runtime");
            Preload(alc, "IKVM.CoreLib");
            Preload(alc, "Android.Compat");
            // Preload critical assemblies using a custom ALC to avoid dynamic emission during resolution
         */

           //(Action)(()=>{
                StartupKt.applicationSetup(folder.AndroidFolder, folder.TempFolder, new AndroidCompatLogManager.LoggerSink(logger));
                AndroidCompatLogManager.SetLoglevel(logger);
           //)).InvokeInJavaContext();
    
            await _manager.SetPreferencesAsync(prefs, cancellationToken);
            _logger.LogInformation("Android App initialized.");
        }
        /*
        private sealed class PreloadingAssemblyLoadContext : AssemblyLoadContext
        {
            public PreloadingAssemblyLoadContext(string name) : base(name, isCollectible: false) { }
            protected override Assembly? Load(AssemblyName assemblyName) => null; // fallback to Default
            public Assembly? TryLoadFromPath(string path)
            {
                try { return LoadFromAssemblyPath(path); } catch { return null; }
            }
        }

        private void PreloadDependencies(PreloadingAssemblyLoadContext alc, IWorkingFolderStructure folder)
        {
            // Known locations for IKVM and AndroidCompat assemblies
            var candidates = new List<string>();
            void AddDlls(string dir)
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
                foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
                    candidates.Add(dll);
            }

            AddDlls(folder.IKVMFolder);
            AddDlls(folder.IKVMToolsFolder);
            AddDlls(folder.AndroidFolder);

            // Prefer specific assemblies first
            string[] preferred = new[]
            {
                "ikvm.runtime.dll",
                "extension.bridge.logging.dll",
                "extension.bridge.dll",
            };

            foreach (var name in preferred)
            {
                var path = candidates.FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
                if (path != null) alc.TryLoadFromPath(path);
            }

            // Then load remaining assemblies to warm the loader (best effort)
            foreach (var dll in candidates)
            {
                alc.TryLoadFromPath(dll);
            }
        }
        */
        private void ShutdownAndroidApp()
        {
            _logger.LogInformation("Android App is shutting down...");

            // Stop Android main looper and unregister sinks/handlers on Kotlin side
            // StartupKt.applicationShutdown should exist per your Kotlin changes
            try
            {
                ((Action)(() => {
                    StartupKt.applicationShutdown(extension.bridge.logging.AndroidCompatLoggerKt.androidCompatLogger(typeof(BridgeManager)));
                })).InvokeInJavaContext();
                alc.Unload();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "applicationShutdown invocation failed.");
            }
            _logger.LogInformation("Android App shut down.");
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bridge Host initializing...");
            AppDomain.CurrentDomain.UnhandledException += (s, e) => _logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                _logger.LogCritical(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            _androidLogger = _loggerFactory.CreateLogger("Android");
            await InitAndroidAppAsync(_folder, _androidLogger, stoppingToken).ConfigureAwait(false);
            await _manager.InitializeAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Bridge Host initialized.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(100, stoppingToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // Ignore
                }
            }
            _logger.LogInformation("Bridge Host shutting down...");
            _manager.Shutdown();
            ShutdownAndroidApp();
            _logger.LogInformation("Bridge Host shut down.");
        }
    }
}
