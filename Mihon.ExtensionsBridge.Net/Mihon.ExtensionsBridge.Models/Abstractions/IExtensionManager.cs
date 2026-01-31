using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IExtensionManager
    {
        Task<RepositoryGroup?> AddExtensionAsync(TachiyomiExtension extension, bool force = false, CancellationToken token = default);
        Task<RepositoryGroup?> AddExtensionAsync(TachiyomiRepository repository, TachiyomiExtension extension, bool force = false, CancellationToken token = default);
        Task<RepositoryGroup?> AddExtensionAsync(byte[] apk, bool force = false, CancellationToken token = default);
        Task<IExtensionInterop> GetInteropAsync(RepositoryGroup entry, CancellationToken token = default);
        Task<List<RepositoryGroup>> ListExtensionsAsync(CancellationToken token = default);
        Task<bool> RemoveExtensionAsync(RepositoryGroup group, CancellationToken token = default);
        Task<RepositoryGroup?> RemoveExtensionVersionAsync(RepositoryEntry entry, CancellationToken token = default);
        Task<RepositoryGroup> SetActiveExtensionVersionAsync(RepositoryGroup group, CancellationToken token = default);

    }
    public interface IInternalExtensionManager : IExtensionManager
    {
        Task<int> ValidateAndRecompileAsync(IEnumerable<RepositoryEntry> entries, CancellationToken token = default);
        Task InitializeAsync(CancellationToken token = default);
        Task<RepositoryGroup?> FindExtensionAsync(RepositoryGroup grp, CancellationToken token = default);
        Task CompareOnlineWithLocalAndAutoUpdateAsync(IEnumerable<TachiyomiRepository> onlineRepos, CancellationToken token = default);
        Task ShutdownAsync(CancellationToken token = default);
    }
}