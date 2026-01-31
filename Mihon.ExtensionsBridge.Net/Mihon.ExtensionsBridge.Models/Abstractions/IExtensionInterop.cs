using Mihon.ExtensionsBridge.Models.Extensions;

namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IExtensionInterop : IDisposable
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        List<ISourceInterop> Sources { get; }
        Task<List<UniquePreference>> LoadPreferencesAsync(CancellationToken token);
        Task SavePreferencesAsync(List<UniquePreference> press, CancellationToken token);
        Task ShutdownAsync(CancellationToken token);
    }
}
