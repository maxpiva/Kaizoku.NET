namespace Mihon.ExtensionsBridge.Models.Abstractions;

public interface IBridgeManager
{
    public bool Initialized { get; }
    /// <summary>
    /// Returns a task that completes when the bridge is fully initialized
    /// (Android compat layer + extension compilation + repository discovery).
    /// Consumers should await this before accessing bridge services.
    /// </summary>
    Task InitializationCompleted { get; }
    IExtensionManager LocalExtensionManager { get; }
    IRepositoryManager OnlineRepositoryManager { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    void Shutdown();
    Task<Preferences> GetPreferencesAsync(CancellationToken cancellationToken);
    Task SetPreferencesAsync(Models.Preferences prefs, CancellationToken cancellationToken);

}
