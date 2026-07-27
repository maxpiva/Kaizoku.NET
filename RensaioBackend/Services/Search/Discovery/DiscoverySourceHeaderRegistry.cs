using System.Collections.Concurrent;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// Process-wide registry of the image-request headers (referer/user-agent/...) each discovery
/// source's own HTTP client would send, keyed by mihonProviderId. Populated during discovery
/// sweeps (workers report them per source; the in-process path reads them off the interop) and
/// used by the image cache to replay a cover fetch with the source's identity when the plain
/// request is rejected and no interop is available in this process (worker-mode sweeps).
/// In-memory only: after a restart the next sweep re-registers them.
/// </summary>
public class DiscoverySourceHeaderRegistry
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _headers =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string mihonProviderId, IReadOnlyDictionary<string, string>? headers)
    {
        if (string.IsNullOrEmpty(mihonProviderId) || headers == null || headers.Count == 0)
            return;
        _headers[mihonProviderId] = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string>? Get(string? mihonProviderId)
    {
        if (string.IsNullOrEmpty(mihonProviderId))
            return null;
        return _headers.TryGetValue(mihonProviderId, out Dictionary<string, string>? headers) ? headers : null;
    }
}
