using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IExtensionManager
    {
        Task<RepositoryGroup?> AddExtensionAsync(TachiyomiExtension extension, bool force = false, CancellationToken token = default);
        Task<RepositoryGroup?> AddExtensionAsync(TachiyomiRepository repository, TachiyomiExtension extension, bool force = false, CancellationToken token = default);
        Task<RepositoryGroup?> AddExtensionAsync(byte[] apk, bool force = false, CancellationToken token = default);
        Task<IExtensionInterop> GetInteropAsync(RepositoryGroup entry, CancellationToken token = default);
        /// <summary>
        /// Shadow-loads an online (not installed) extension for discovery searches and returns an interop for it.
        /// The extension is downloaded and converted into a private discovery cache folder; it is NEVER
        /// registered in the local extension repository, so it does not appear as installed anywhere.
        /// If the extension happens to be installed already, the normal installed interop is returned instead.
        /// </summary>
        /// <param name="extension">The online repository extension to shadow-load.</param>
        /// <param name="token">Cancellation token for the caller's wait; a shared load keeps running in the background.</param>
        Task<IExtensionInterop> GetDiscoveryInteropAsync(TachiyomiExtension extension, CancellationToken token = default);
        /// <summary>
        /// Returns the ALREADY shadow-loaded discovery interop for the given package, or null when
        /// none is loaded. Never triggers a download/convert/classload — intended for opportunistic
        /// reuse (e.g. fetching discovery result covers through the source's own HTTP client).
        /// </summary>
        /// <param name="package">The extension package name (e.g. eu.kanade.tachiyomi.extension.en.foo).</param>
        IExtensionInterop? TryGetLoadedDiscoveryInterop(string package);
        List<RepositoryGroup> ListExtensions();
        RepositoryGroup? FindExtension(string name);
        Task<bool> RemoveExtensionAsync(RepositoryGroup group, CancellationToken token = default);
        Task<RepositoryGroup?> RemoveExtensionVersionAsync(RepositoryEntry entry, CancellationToken token = default);
        Task<RepositoryGroup> SetActiveExtensionVersionAsync(RepositoryGroup group, CancellationToken token = default);

    }
}