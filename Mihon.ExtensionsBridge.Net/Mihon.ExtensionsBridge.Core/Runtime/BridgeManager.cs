using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using extension.bridge;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Core.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Runtime
{


    public class BridgeManager : IBridgeManager
    {
        private readonly IWorkingFolderStructure _workingFolderStructure;
        private readonly ILogger _logger;

        private readonly IExtensionManager _extensionsManager;
        private readonly IRepositoryManager _repositoryManager;
        private readonly IInternalExtensionManager _internalExtensionsManager;
        private readonly IInternalRepositoryManager _internalRepositoryManager;
        private readonly IServiceProvider _serviceProvider;
        private bool _initialized = false;

        public BridgeManager(IServiceProvider serviceProvider,
            IWorkingFolderStructure workingFolderStructure,
            IRepositoryManager repositoryManager,
            IExtensionManager extensionsManager,
            IInternalRepositoryManager internalRepositoryManager,
            IInternalExtensionManager internalExtensionsManager,
            ILogger<BridgeManager> logger)
        {
            _workingFolderStructure = workingFolderStructure;
            _repositoryManager = repositoryManager;
            _logger = logger;
            _extensionsManager = extensionsManager;
            _serviceProvider = serviceProvider;
            _internalExtensionsManager = internalExtensionsManager;
            _internalRepositoryManager = internalRepositoryManager;
        }

        private Dictionary<string, Dictionary<string, string>> MapMapToDictionary(java.util.Map map)
        {
            Dictionary<string, Dictionary<string, string>> overrides = [];
            foreach (string n in map.keySet().toArray())
            {
                java.util.Map innerMap = (java.util.Map)map.get(n);
                foreach (string k in innerMap.keySet().toArray())
                {
                    if (!overrides.ContainsKey(n))
                    {
                        overrides[n] = [];
                    }
                    overrides[n][k] = (string)innerMap.get(k);
                }
            }
            return overrides;
        }
        private java.util.Map DictionaryToMapMap(Dictionary<string, Dictionary<string, string>> dict)
        {
            java.util.Map map = new java.util.HashMap();
            foreach (var n in dict)
            {
                java.util.Map innerMap = new java.util.HashMap();
                foreach (var k in n.Value)
                {
                    innerMap.put(k.Key, k.Value);
                }
                map.put(n.Key, innerMap);
            }
            return map;
        }
        public bool IsDictionaryEqual(Dictionary<string, Dictionary<string, string>> src, Dictionary<string, Dictionary<string, string>> dst)
        {
            if (src.Count != dst.Count)
                return false;
            foreach (var n in src)
            {
                if (!dst.ContainsKey(n.Key))
                    return false;
                var innerSrc = n.Value;
                var innerDst = dst[n.Key];
                if (innerSrc.Count != innerDst.Count)
                    return false;
                foreach (var k in innerSrc)
                {
                    if (!innerDst.ContainsKey(k.Key))
                        return false;
                    if (innerDst[k.Key] != k.Value)
                        return false;
                }
            }
            return true;
        }
        public async Task SetPreferencesAsync(Mihon.ExtensionsBridge.Models.Preferences prefs, CancellationToken cancellationToken)
        {
            // ((Action)(() => {
                SettingsConfig.Settings config = new SettingsConfig.Settings();
                try
                {
                    config = ConfigKt.getSettings();
                }
                catch 
                {
                    // If we fail to load the config, we can assume it's the default config and continue with that. This can happen if the config file is missing or corrupted.
                }
                Dictionary<string, Dictionary<string, string>> overrides = MapMapToDictionary(config.getInterceptorOverrides());
                bool update = false;
                if (!IsDictionaryEqual(prefs.Interceptors, overrides))
                {
                    config.setInterceptorOverrides(DictionaryToMapMap(prefs.Interceptors));
                    update = true;
                }
                if (prefs.FlareSolverr != null)
                {
                    if (prefs.FlareSolverr.Enabled != config.getFlareSolverrEnabled())
                    {
                        config.setFlareSolverrEnabled(prefs.FlareSolverr.Enabled);
                        update = true;
                    }
                    if (prefs.FlareSolverr.Url != config.getFlareSolverrUrl())
                    {
                        config.setFlareSolverrUrl(prefs.FlareSolverr.Url ?? "http://127.0.0.1:8189");
                        update = true;
                    }
                    if (prefs.FlareSolverr.Timeout != config.getFlareSolverrTimeout())
                    {
                        config.setFlareSolverrTimeout(prefs.FlareSolverr.Timeout);
                        update = true;
                    }
                    if (prefs.FlareSolverr.SessionName != config.getFlareSolverrSessionName())
                    {
                        config.setFlareSolverrSessionName(prefs.FlareSolverr.SessionName);
                        update = true;
                    }
                    if (prefs.FlareSolverr.SessionTtl != config.getFlareSolverrSessionTtl())
                    {
                        config.setFlareSolverrSessionTtl(prefs.FlareSolverr.SessionTtl);
                        update = true;
                    }
                    if (prefs.FlareSolverr.AsResponseFallback != config.getFlareSolverrAsResponseFallback())
                    {
                        config.setFlareSolverrAsResponseFallback(prefs.FlareSolverr.AsResponseFallback);
                        update = true;
                    }
                }
                if (prefs.SocksProxy != null)
                {
                    if (prefs.SocksProxy.Enabled != config.getSocksProxyEnabled())
                    {
                        config.setSocksProxyEnabled(prefs.SocksProxy.Enabled);
                        update = true;
                    }
                    int port = 0;
                    string originalPort = (string)config.getSocksProxyPort();
                    int.TryParse(originalPort, out port);
                    if (prefs.SocksProxy.Port != port)
                    {
                        string set = originalPort.ToString();
                        if (set == "0")
                            set = "";
                        config.setSocksProxyPort(set);
                        update = true;
                    }
                    if (prefs.SocksProxy.Host != config.getSocksProxyHost())
                    {
                        config.setSocksProxyHost(prefs.SocksProxy.Host ?? "");
                        update = true;
                    }
                    if (prefs.SocksProxy.Username != config.getSocksProxyUsername())
                    {
                        config.setSocksProxyUsername(prefs.SocksProxy.Username);
                        update = true;
                    }
                    if (prefs.SocksProxy.Password != config.getSocksProxyPassword())
                    {
                        config.setSocksProxyPassword(prefs.SocksProxy.Password);
                        update = true;
                    }
                }
                if (update)
                {
                    ConfigKt.setSettings(config);
                }
              //  SettingsConfig.Settings config2 = ConfigKt.getSettings();
            //})).InvokeInJavaContext();
            

            await _workingFolderStructure.SavePreferencesAsync(prefs, cancellationToken);
        }
        public async Task<Mihon.ExtensionsBridge.Models.Preferences> GetPreferencesAsync(CancellationToken cancellationToken)
        {
            Mihon.ExtensionsBridge.Models.Preferences? prefs = await _workingFolderStructure.LoadPreferencesAsync(cancellationToken);
            if (prefs == null)
                return new Mihon.ExtensionsBridge.Models.Preferences();
            return prefs;
        }



        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
            {
                _logger.LogWarning("Bridge Manager is already initialized.");
                return;
            }
            _logger.LogInformation("Bridge Manager initializing...");
            await _internalRepositoryManager.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await _internalExtensionsManager.InitializeAsync(cancellationToken).ConfigureAwait(false);
            List<RepositoryEntry> entries = _internalExtensionsManager.ListExtensions().SelectMany(a => a.Entries).ToList();
            await _internalExtensionsManager.ValidateAndRecompileAsync(entries, cancellationToken).ConfigureAwait(false);
            await _internalRepositoryManager.RefreshAllRepositoriesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Bridge Manager initialized.");
            _initialized = true;
        }

        public IExtensionManager LocalExtensionManager => _initialized ? _extensionsManager : throw new InvalidOperationException("Bridge Manager is not initialized.");
        public IRepositoryManager OnlineRepositoryManager => _initialized ? _repositoryManager : throw new InvalidOperationException("Bridge Manager is not initialized.");

        public bool Initialized => _initialized;

        public void Shutdown()
        {
            try
            {
                _logger.LogInformation("BridgeManager is shuting down.");
                // Best-effort: shutdown all extension interops via the internal manager
                try
                {
                    _internalExtensionsManager.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while shutting down extension interops.");
                }
              
                _logger.LogInformation("BridgeManager shut down.");
            }
            catch
            {
                // swallow to avoid throwing during host shutdown
            }
        }
    }
}
