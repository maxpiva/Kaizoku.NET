using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;

namespace KaizokuBackend.Services.Bridge
{
    public class MihonBridgeService : IExtensionManager, IRepositoryManager
    {
        private readonly IBridgeManager _bridgeManager;
        private readonly IWorkingFolderStructure _workingFolderStructure;
        private readonly ILogger _logger;

        private ConcurrentDictionary<string, Lazy<Task<IExtensionInterop>>> extOps = [];
        
        public Task<Preferences> GetPreferencesAsync(CancellationToken cancellationToken) => _bridgeManager.GetPreferencesAsync(cancellationToken);
        public Task SetPreferencesAsync(Preferences prefs, CancellationToken cancellationToken) => _bridgeManager.SetPreferencesAsync(prefs, cancellationToken);

        public MihonBridgeService(ILogger<MihonBridgeService> logger, IBridgeManager bridgeManager, IWorkingFolderStructure workingFolderStructure)
        {

            _logger = logger;
            _bridgeManager = bridgeManager;
            _workingFolderStructure = workingFolderStructure;
        }
        public async Task<T?> MihonErrorWrapperAsync<T>(Func<Task<T>> func, string errorMessage, params object[] pars) where T : class, new()
        {
            try
            {
                return await func().ConfigureAwait(false);
            }
            catch (HttpRequestException httpEx)
            {
                object[] pars2 = pars.ToArray();
                Array.Resize(ref pars2, pars2.Length + 1);
                pars2[^1] = httpEx.StatusCode ?? HttpStatusCode.InternalServerError;
                _logger.LogError(errorMessage + " Http Error: {httperror}", pars2);
                return null;
            }
            catch (TaskCanceledException)
            {
                _logger.LogError(errorMessage + " Task was cancelled", pars);
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError(errorMessage + " Operation was cancelled", pars);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, errorMessage, pars);
                return null;
            }
        }
        private async Task<IExtensionInterop> GetFromNameAsync(string name, CancellationToken token = default)
        {
            Lazy<Task<IExtensionInterop>> value = extOps.GetOrAdd(name, (nam) =>
            {
                var allLocal = _bridgeManager.LocalExtensionManager.ListExtensions();
                var repo = allLocal.FirstOrDefault(a => a.Name.Equals(nam, StringComparison.OrdinalIgnoreCase));
                if (repo == null)
                    throw new InvalidOperationException($"Extension '{nam}' not found");
                return new Lazy<Task<IExtensionInterop>>(_bridgeManager.LocalExtensionManager.GetInteropAsync(repo, token));
            });
            return await value.Value.ConfigureAwait(false);
        }
        private async Task<IExtensionInterop> GetFromPackageAsync(string package, CancellationToken token = default)
        {
            var allLocal = _bridgeManager.LocalExtensionManager.ListExtensions();
            var repo = allLocal.FirstOrDefault(a => a.GetActiveEntry().Extension.Package.Equals(package, StringComparison.OrdinalIgnoreCase));
            if (repo==null)
            {
                throw new InvalidOperationException("Package not found");
            }
            Lazy<Task<IExtensionInterop>> value = extOps.GetOrAdd(repo.Name, (nam) =>
            {
                var allLocal = _bridgeManager.LocalExtensionManager.ListExtensions();
                var repo = allLocal.FirstOrDefault(a => a.Name.Equals(nam, StringComparison.OrdinalIgnoreCase));
                if (repo == null)
                    throw new InvalidOperationException($"Extension '{nam}' not found");
                return new Lazy<Task<IExtensionInterop>>(_bridgeManager.LocalExtensionManager.GetInteropAsync(repo, token));
            });
            return await value.Value.ConfigureAwait(false);
        }
        private async Task<ISourceInterop> GetFromNameAndSourceAsync(string nameandsource, CancellationToken token = default)
        {
            string[] split = nameandsource.Split("|");
            if (split.Length < 2)
                throw new InvalidOperationException("Invalid Name And Source");
            long source = 0;
            if (!long.TryParse(split[1], out source))
                throw new InvalidOperationException("Invalid Source Id");
            string name = split[0];
            IExtensionInterop extOp = await GetFromNameAsync(name, token).ConfigureAwait(false);
            if (extOp == null)
                throw new InvalidOperationException($"Extension '{name}' not found for source '{source}'");
            ISourceInterop? src = extOp.Sources.FirstOrDefault(a => a.Id == source);
            if (src == null)
                throw new InvalidOperationException($"Source '{source}' not found in extension '{name}'");
            return src!;
        }
        private async Task<ISourceInterop> GetFromMihonProviderIdAsync(string mihonproviderId, CancellationToken token = default)
        {
            string[] split = mihonproviderId.Split("|");
            if (split.Length < 2)
                throw new InvalidOperationException("Invalid Package And Source");
            long source = 0;
            if (!long.TryParse(split[1], out source))
                throw new InvalidOperationException("Invalid Source Id");
            string package = split[0];
            IExtensionInterop extOp = await GetFromPackageAsync(package, token).ConfigureAwait(false);
            if (extOp == null)
                throw new InvalidOperationException($"Extension '{package}' not found for source '{source}'");
            ISourceInterop? src = extOp.Sources.FirstOrDefault(a => a.Id == source);
            if (src == null)
                throw new InvalidOperationException($"Source '{source}' not found in extension '{package}'");
            return src!;
        }


        public Task<ISourceInterop> SourceFromProviderIdAsync(string mihonProviderName, CancellationToken token = default)
        {
            return GetFromMihonProviderIdAsync(mihonProviderName, token);
        }

        public Task<RepositoryGroup?> AddExtensionAsync(TachiyomiExtension extension, bool force = false, CancellationToken token = default)
        {
            return _bridgeManager.LocalExtensionManager.AddExtensionAsync(extension, force, token);
        }

        public Task<RepositoryGroup?> AddExtensionAsync(TachiyomiRepository repository, TachiyomiExtension extension, bool force = false, CancellationToken token = default)
        {
            return _bridgeManager.LocalExtensionManager.AddExtensionAsync(repository, extension, force, token);
        }

        public Task<RepositoryGroup?> AddExtensionAsync(byte[] apk, bool force = false, CancellationToken token = default)
        {
            return _bridgeManager.LocalExtensionManager.AddExtensionAsync(apk, force, token);
        }

        public Task<IExtensionInterop> GetInteropAsync(RepositoryGroup entry, CancellationToken token = default)
        {
            return GetFromNameAsync(entry.Name, token);
        }

        public List<RepositoryGroup> ListExtensions()
        {
            return _bridgeManager.LocalExtensionManager.ListExtensions();
        }

        public RepositoryGroup? FindExtension(string name)
        {
            return _bridgeManager.LocalExtensionManager.FindExtension(name);
        }

        public Task<bool> RemoveExtensionAsync(RepositoryGroup group, CancellationToken token = default)
        {
            return _bridgeManager.LocalExtensionManager.RemoveExtensionAsync(group, token);
        }

        public Task<RepositoryGroup?> RemoveExtensionVersionAsync(RepositoryEntry entry, CancellationToken token = default)
        {
            return _bridgeManager.LocalExtensionManager.RemoveExtensionVersionAsync(entry, token);
        }

        public Task<RepositoryGroup> SetActiveExtensionVersionAsync(RepositoryGroup group, CancellationToken token = default)
        {
            return _bridgeManager.LocalExtensionManager.SetActiveExtensionVersionAsync(group, token);
        }

        public Task<TachiyomiRepository> AddOnlineRepositoryAsync(TachiyomiRepository repository, CancellationToken token = default)
        {
            return _bridgeManager.OnlineRepositoryManager.AddOnlineRepositoryAsync(repository, token);
        }

        public List<TachiyomiRepository> ListOnlineRepositories()
        {
            return _bridgeManager.OnlineRepositoryManager.ListOnlineRepositories();
        }

        public Task RefreshAllRepositoriesAsync(CancellationToken token = default)
        {
            return _bridgeManager.OnlineRepositoryManager.RefreshAllRepositoriesAsync(token);
        }

        public Task<bool> RemoveOnlineRespositoryAsync(TachiyomiRepository repository, CancellationToken token = default)
        {
            return _bridgeManager.OnlineRepositoryManager.RemoveOnlineRespositoryAsync(repository, token);
        }
    }
}
