using System.Globalization;
using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Providers;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Bridge;

/// <summary>
/// Ensures persisted providers and latest-series rows contain the bridge metadata required for Mihon-only workflows.
/// </summary>
public class BridgeMetadataBackfillService
{
    private readonly AppDbContext _db;
    private readonly ProviderCacheService _providerCache;
    private readonly ILogger<BridgeMetadataBackfillService> _logger;

    public BridgeMetadataBackfillService(AppDbContext db, ProviderCacheService providerCache, ILogger<BridgeMetadataBackfillService> logger)
    {
        _db = db;
        _providerCache = providerCache;
        _logger = logger;
    }

    public async Task EnsureMetadataAsync(CancellationToken token = default)
    {
        var sources = await BuildSourceIndexAsync(token).ConfigureAwait(false);
        if (sources.IsEmpty)
        {
            _logger.LogDebug("No provider sources available; skipping bridge metadata backfill.");
            return;
        }

        var providerUpdates = await BackfillSeriesProvidersAsync(sources, token).ConfigureAwait(false);
        var latestUpdates = await BackfillLatestSeriesAsync(sources, token).ConfigureAwait(false);

        if (providerUpdates == 0 && latestUpdates == 0)
        {
            _logger.LogDebug("All SeriesProvider and LatestSerie rows already contain bridge metadata.");
            return;
        }

        _logger.LogInformation(
            "Bridge metadata backfill updated {ProviderCount} SeriesProvider rows and {LatestCount} LatestSerie rows.",
            providerUpdates,
            latestUpdates);
    }

    private async Task<SourceIndex> BuildSourceIndexAsync(CancellationToken token)
    {
        var storages = await _providerCache.GetCachedProvidersAsync(token).ConfigureAwait(false);
        var byId = new Dictionary<long, SourceMetadata>();
        var byAlias = new Dictionary<string, SourceMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var storage in storages)
        {
            foreach (var mapping in storage.Mappings)
            {
                if (mapping.Source == null)
                {
                    continue;
                }

                var source = mapping.Source;
                var sourceId = source.ExtensionSourceId != 0 ? source.ExtensionSourceId : ParseSourceId(source.Id);
                if (sourceId == 0)
                {
                    continue;
                }

                var metadata = new SourceMetadata(
                    string.IsNullOrWhiteSpace(source.ExtensionPackageId) ? storage.PkgName : source.ExtensionPackageId,
                    source.ExtensionRepositoryId,
                    sourceId,
                    source.Name,
                    source.DisplayName,
                    source.Lang);

                byId.TryAdd(metadata.SourceId, metadata);
                foreach (var key in EnumerateAliasKeys(storage, metadata))
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        byAlias.TryAdd(key!, metadata);
                    }
                }
            }
        }

        return new SourceIndex(byId, byAlias);
    }

    private async Task<int> BackfillSeriesProvidersAsync(SourceIndex sources, CancellationToken token)
    {
        var candidates = await _db.SeriesProviders
            .Where(sp => string.IsNullOrWhiteSpace(sp.ExtensionPackageId) || sp.ExtensionSourceId == 0 || string.IsNullOrWhiteSpace(sp.BridgeSeriesId))
            .ToListAsync(token)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var updated = 0;
        foreach (var provider in candidates)
        {
            var source = ResolveSourceMetadata(provider.ExtensionSourceId, null, provider.Provider, provider.Language, sources);
            var changed = ApplySourceMetadata(provider, source);

            if (string.IsNullOrWhiteSpace(provider.BridgeSeriesId) &&
                !string.IsNullOrWhiteSpace(provider.ExtensionPackageId) &&
                provider.ExtensionSourceId != 0)
            {
                provider.BridgeSeriesId = BridgeKeyUtility.BuildSeriesId(
                    provider.ExtensionPackageId,
                    provider.ExtensionSourceId,
                    ResolveProviderRemoteKey(provider));
                changed = true;
            }

            if (changed)
            {
                updated++;
            }
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            _logger.LogDebug("Updated {Count} SeriesProvider rows with bridge metadata.", updated);
        }

        return updated;
    }

    private async Task<int> BackfillLatestSeriesAsync(SourceIndex sources, CancellationToken token)
    {
        var candidates = await _db.LatestSeries
            .Where(ls => string.IsNullOrWhiteSpace(ls.ExtensionPackageId) || ls.ExtensionSourceId == 0 || string.IsNullOrWhiteSpace(ls.BridgeSeriesId))
            .ToListAsync(token)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var updated = 0;
        foreach (var latest in candidates)
        {
            var source = ResolveSourceMetadata(latest.ExtensionSourceId, latest.SuwayomiSourceId, latest.Provider, latest.Language, sources);
            var changed = ApplySourceMetadata(latest, source);

            if (string.IsNullOrWhiteSpace(latest.BridgeSeriesId) &&
                !string.IsNullOrWhiteSpace(latest.ExtensionPackageId) &&
                latest.ExtensionSourceId != 0)
            {
                latest.BridgeSeriesId = BridgeKeyUtility.BuildSeriesId(
                    latest.ExtensionPackageId,
                    latest.ExtensionSourceId,
                    ResolveLatestRemoteKey(latest));
                changed = true;
            }

            if (changed)
            {
                updated++;
            }
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            _logger.LogDebug("Updated {Count} LatestSerie rows with bridge metadata.", updated);
        }

        return updated;
    }

    private static SourceMetadata? ResolveSourceMetadata(long currentSourceId, string? fallbackSourceId, string providerName, string language, SourceIndex index)
    {
        if (currentSourceId > 0 && index.ById.TryGetValue(currentSourceId, out var fromCurrent))
        {
            return fromCurrent;
        }

        var parsedFallback = ParseSourceId(fallbackSourceId);
        if (parsedFallback > 0 && index.ById.TryGetValue(parsedFallback, out var fromFallback))
        {
            return fromFallback;
        }

        var aliasKey = BuildAliasKey(providerName, language);
        if (!string.IsNullOrEmpty(aliasKey) && index.Aliases.TryGetValue(aliasKey, out var fromAlias))
        {
            return fromAlias;
        }

        return null;
    }

    private static bool ApplySourceMetadata(SeriesProvider provider, SourceMetadata? metadata)
    {
        if (metadata == null)
        {
            return false;
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(provider.ExtensionPackageId) && !string.IsNullOrWhiteSpace(metadata.PackageId))
        {
            provider.ExtensionPackageId = metadata.PackageId;
            changed = true;
        }

        if (provider.ExtensionSourceId == 0 && metadata.SourceId > 0)
        {
            provider.ExtensionSourceId = metadata.SourceId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(provider.ExtensionRepositoryId) && !string.IsNullOrWhiteSpace(metadata.RepositoryId))
        {
            provider.ExtensionRepositoryId = metadata.RepositoryId;
            changed = true;
        }

        return changed;
    }

    private static bool ApplySourceMetadata(LatestSerie latest, SourceMetadata? metadata)
    {
        if (metadata == null)
        {
            return false;
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(latest.ExtensionPackageId) && !string.IsNullOrWhiteSpace(metadata.PackageId))
        {
            latest.ExtensionPackageId = metadata.PackageId;
            changed = true;
        }

        if (latest.ExtensionSourceId == 0 && metadata.SourceId > 0)
        {
            latest.ExtensionSourceId = metadata.SourceId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(latest.ExtensionRepositoryId) && !string.IsNullOrWhiteSpace(metadata.RepositoryId))
        {
            latest.ExtensionRepositoryId = metadata.RepositoryId;
            changed = true;
        }

        return changed;
    }

    private static string ResolveProviderRemoteKey(SeriesProvider provider)
    {
        if (provider.SuwayomiId != 0)
        {
            return provider.SuwayomiId.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(provider.Url))
        {
            return provider.Url!;
        }

        return string.IsNullOrWhiteSpace(provider.Title) ? provider.Id.ToString() : provider.Title;
    }

    private static string ResolveLatestRemoteKey(LatestSerie latest)
    {
        if (latest.SuwayomiId != 0)
        {
            return latest.SuwayomiId.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(latest.Url))
        {
            return latest.Url!;
        }

        return latest.Title;
    }

    private static IEnumerable<string?> EnumerateAliasKeys(ProviderStorage storage, SourceMetadata metadata)
    {
        yield return BuildAliasKey(metadata.Name, metadata.Language);
        yield return BuildAliasKey(metadata.DisplayName, metadata.Language);
        yield return BuildAliasKey(storage.Name, metadata.Language);
    }

    private static string? BuildAliasKey(string? value, string? language)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedName = value.Trim().ToLowerInvariant();
        var normalizedLang = string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim().ToLowerInvariant();
        return string.Create(normalizedName.Length + normalizedLang.Length + 1, (normalizedName, normalizedLang), static (span, state) =>
        {
            var (name, lang) = state;
            name.AsSpan().CopyTo(span);
            span = span[name.Length..];
            span[0] = '|';
            span = span[1..];
            lang.AsSpan().CopyTo(span);
        });
    }

    private static long ParseSourceId(string? value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private sealed record SourceMetadata(string PackageId, string? RepositoryId, long SourceId, string? Name, string? DisplayName, string? Language);

    private sealed record SourceIndex(Dictionary<long, SourceMetadata> ById, Dictionary<string, SourceMetadata> Aliases)
    {
        public bool IsEmpty => ById.Count == 0 && Aliases.Count == 0;
    }
}
