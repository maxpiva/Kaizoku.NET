using System.Globalization;

namespace KaizokuBackend.Services.Bridge;

/// <summary>
/// Utility helpers to build deterministic identifiers for bridge-managed entities.
/// </summary>
public static class BridgeKeyUtility
{
    public static string BuildSeriesId(string? packageId, long sourceId, string? remoteId)
    {
        var normalizedPackage = string.IsNullOrWhiteSpace(packageId) ? "legacy" : packageId.Trim();
        var normalizedSource = sourceId > 0 ? sourceId.ToString(CultureInfo.InvariantCulture) : "0";
        var normalizedRemote = string.IsNullOrWhiteSpace(remoteId) ? "unknown" : remoteId.Trim();
        return string.Create(normalizedPackage.Length + normalizedSource.Length + normalizedRemote.Length + 2, (normalizedPackage, normalizedSource, normalizedRemote), static (span, state) =>
        {
            var (pkg, src, remote) = state;
            pkg.AsSpan().CopyTo(span);
            span = span[pkg.Length..];
            span[0] = ':';
            span = span[1..];
            src.AsSpan().CopyTo(span);
            span = span[src.Length..];
            span[0] = ':';
            span = span[1..];
            remote.AsSpan().CopyTo(span);
        });
    }
}
