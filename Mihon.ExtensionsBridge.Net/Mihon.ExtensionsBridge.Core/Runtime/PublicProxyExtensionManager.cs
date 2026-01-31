using android.view;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Runtime
{
    public class PublicProxyExtensionManager : IExtensionManager
    {
        private readonly IInternalExtensionManager _internalExtensionManager;
        private readonly IInternalRepositoryManager _internalRepositoryManager;
        public PublicProxyExtensionManager(IInternalExtensionManager internalExtensionManager, IInternalRepositoryManager internalRepositoryManager)
        {
            _internalExtensionManager = internalExtensionManager;
            _internalRepositoryManager = internalRepositoryManager;
        }
        public async Task<RepositoryGroup?> AddExtensionAsync(TachiyomiExtension extension, bool force = false, CancellationToken token = default)
        {
            RepositoryGroup? grp = await _internalExtensionManager.AddExtensionAsync(extension, force, token).ConfigureAwait(false);
            return grp?.Clone();
        }

        public async Task<RepositoryGroup?> AddExtensionAsync(TachiyomiRepository repository, TachiyomiExtension extension, bool force = false, CancellationToken token = default)
        {
            (TachiyomiRepository? gg, TachiyomiExtension? ext) = _internalRepositoryManager.FindRealRepository(repository, extension);
            if (gg == null || ext == null)
                throw new InvalidOperationException("The provided repository or extension could not be found in the internal repository manager.");
            RepositoryGroup? grp = await _internalExtensionManager.AddExtensionAsync(gg, ext, force, token).ConfigureAwait(false);
            return grp?.Clone();
        }

        public async Task<RepositoryGroup?> AddExtensionAsync(byte[] apk, bool force = false, CancellationToken token = default)
        {
            var repo = await _internalExtensionManager.AddExtensionAsync(apk, force, token).ConfigureAwait(false);
            return repo?.Clone();
        }

        public Task<IExtensionInterop> GetInteropAsync(RepositoryGroup entry, CancellationToken token = default)
        {
            return _internalExtensionManager.GetInteropAsync(entry, token);
        }

        public async Task<List<RepositoryGroup>> ListExtensionsAsync(CancellationToken token = default)
        {
            List<RepositoryGroup> grps = await _internalExtensionManager.ListExtensionsAsync(token).ConfigureAwait(false);
            return grps.ConvertAll(x => x.Clone());
        }

        public async Task<bool> RemoveExtensionAsync(RepositoryGroup group, CancellationToken token = default)
        {
            RepositoryGroup? internalGroup = await _internalExtensionManager.FindExtensionAsync(group, token).ConfigureAwait(false);
            if (internalGroup == null)
                return false;
            return await _internalExtensionManager.RemoveExtensionAsync(internalGroup, token).ConfigureAwait(false);
        }
        public async Task<RepositoryGroup?> RemoveExtensionVersionAsync(RepositoryEntry entry, CancellationToken token = default)
        {
            RepositoryGroup? internalGroup = await _internalExtensionManager.RemoveExtensionVersionAsync(entry, token).ConfigureAwait(false);
            return internalGroup?.Clone();
        }

        public async Task<RepositoryGroup> SetActiveExtensionVersionAsync(RepositoryGroup group, CancellationToken token = default)
        {
            RepositoryGroup internalGroup = await _internalExtensionManager.SetActiveExtensionVersionAsync(group, token).ConfigureAwait(false);
            return internalGroup!.Clone();
        }
    }
}
