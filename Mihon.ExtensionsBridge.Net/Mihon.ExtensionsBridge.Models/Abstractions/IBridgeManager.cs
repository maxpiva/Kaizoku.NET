namespace Mihon.ExtensionsBridge.Models.Abstractions;

public interface IBridgeManager 
{
    public bool Initialized { get; }
    IExtensionManager LocalExtensionManager { get; }
    IRepositoryManager OnlineRepositoryManager { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    void Shutdown();
    Task<Preferences> GetPreferencesAsync(CancellationToken cancellationToken);
    Task SetPreferencesAsync(Models.Preferences prefs, CancellationToken cancellationToken);

}
