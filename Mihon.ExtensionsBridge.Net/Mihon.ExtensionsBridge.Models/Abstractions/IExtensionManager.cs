using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IExtensionManager
    {
        Task<RepositoryGroup?> AddExtensionAsync(TachiyomiExtension extension, bool force = false, CancellationToken token = default);
        Task<RepositoryGroup?> AddExtensionAsync(TachiyomiRepository repository, TachiyomiExtension extension, bool force = false, CancellationToken token = default);
        Task<RepositoryGroup?> AddExtensionAsync(byte[] apk, bool force = false, CancellationToken token = default);
        Task<IExtensionInterop> GetInteropAsync(RepositoryGroup entry, CancellationToken token = default);
        List<RepositoryGroup> ListExtensions();
        RepositoryGroup? FindExtension(string name);
        Task<bool> RemoveExtensionAsync(RepositoryGroup group, CancellationToken token = default);
        Task<RepositoryGroup?> RemoveExtensionVersionAsync(RepositoryEntry entry, CancellationToken token = default);
        Task<RepositoryGroup> SetActiveExtensionVersionAsync(RepositoryGroup group, CancellationToken token = default);

    }
}