using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mihon.ExtensionsBridge.Core.Preferences;

/// <summary>
/// Simplified preference store bridging Android SharedPreferences semantics.
/// </summary>
public interface IPreferenceStore
{
    ValueTask SetStringAsync(string key, string value, CancellationToken cancellationToken = default);

    ValueTask<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    ValueTask SetLongAsync(string key, long value, CancellationToken cancellationToken = default);

    ValueTask<long?> GetLongAsync(string key, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}
