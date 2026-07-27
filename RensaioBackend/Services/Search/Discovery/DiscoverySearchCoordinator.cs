using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using RensaioBackend.Hubs;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Images;
using RensaioBackend.Services.Settings;
using System.Collections.Concurrent;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// Singleton orchestrator for automatic, streaming discovery sweeps.
///
/// The client calls <see cref="StartOrAttachAsync"/> with the user's query; installed-source search
/// is untouched and renders immediately, while this starts (or attaches to) a background sweep over
/// all eligible not-installed extensions. Incremental results and per-extension progress stream to
/// clients as "DiscoverySearch" events over the existing /progress SignalR hub (mirroring the
/// JobHubReportService pattern), keyed by searchId. Completed sweeps are cached for
/// <see cref="ResultCacheDuration"/> so dialog back-and-forth never re-sweeps; cancelled sweeps are
/// deliberately NOT cached (partial sets would otherwise masquerade as complete for 15 minutes).
/// </summary>
public class DiscoverySearchCoordinator
{
    public const string HubEventName = "DiscoverySearch";
    private static readonly TimeSpan ResultCacheDuration = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ProgressHub> _hub;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DiscoverySearchCoordinator> _logger;

    private sealed class Sweep
    {
        public required string SearchId { get; init; }
        public required string Key { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public ConcurrentDictionary<string, DiscoverySeriesDto> Results { get; } = new();
        public int TotalExtensions;
        public int TotalSources;
        public int CompletedExtensions;
        public string Stage = DiscoveryStreamCallbacks.StagePreparing;
    }

    private readonly object _startLock = new();
    private readonly ConcurrentDictionary<string, Sweep> _byKey = new();
    private readonly ConcurrentDictionary<string, Sweep> _byId = new();

    public DiscoverySearchCoordinator(
        IServiceScopeFactory scopeFactory,
        IHubContext<ProgressHub> hub,
        IMemoryCache memoryCache,
        ILogger<DiscoverySearchCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    private static string BuildKey(string keyword, IEnumerable<string> languages)
        => keyword.Trim().ToLowerInvariant() + "|" +
           string.Join(',', languages.Select(l => l.ToLowerInvariant()).OrderBy(l => l, StringComparer.Ordinal));

    private static string CacheKey(string key) => "DS:" + key;

    /// <summary>
    /// Starts a streaming discovery sweep for the query, or attaches to the identical sweep already
    /// running, or returns the cached complete result. Never blocks on the sweep itself.
    /// </summary>
    public async Task<DiscoveryStartDto> StartOrAttachAsync(string keyword, List<string>? languages, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new DiscoveryStartDto { Done = true };

        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var settings = await settingsService.GetSettingsAsync(token).ConfigureAwait(false);
        if (!settings.DiscoveryIncludeInSearch)
            return new DiscoveryStartDto { Enabled = false, Done = true };

        var service = scope.ServiceProvider.GetRequiredService<DiscoverySearchService>();
        List<string> normalizedLanguages = await service.NormalizeLanguagesAsync(languages, token).ConfigureAwait(false);
        string key = BuildKey(keyword, normalizedLanguages);

        if (_memoryCache.TryGetValue(CacheKey(key), out List<DiscoverySeriesDto>? cached))
        {
            return new DiscoveryStartDto { Done = true, Results = cached! };
        }

        // Counts are cheap (in-memory list scans) and let the client label the progress affordance
        // from the very first render.
        DiscoverySourcesDto counts = await service.GetDiscoverySourcesAsync(normalizedLanguages, token).ConfigureAwait(false);
        if (counts.ExtensionCount == 0)
            return new DiscoveryStartDto { Done = true };

        Sweep sweep;
        bool created = false;
        lock (_startLock)
        {
            if (!_byKey.TryGetValue(key, out sweep!))
            {
                sweep = new Sweep
                {
                    SearchId = Guid.NewGuid().ToString("N"),
                    Key = key,
                    Cts = new CancellationTokenSource(),
                    TotalExtensions = counts.ExtensionCount,
                    TotalSources = counts.SourceCount
                };
                _byKey[key] = sweep;
                _byId[sweep.SearchId] = sweep;
                created = true;
            }
        }
        if (created)
        {
            string queryForLog = keyword;
            _ = Task.Run(() => RunSweepAsync(sweep, queryForLog, normalizedLanguages), CancellationToken.None);
            _logger.LogInformation("Discovery sweep {SearchId} started for '{Keyword}' ({Extensions} extensions, {Sources} sources).",
                sweep.SearchId, keyword, counts.ExtensionCount, counts.SourceCount);
        }

        return new DiscoveryStartDto
        {
            SearchId = sweep.SearchId,
            Done = false,
            Stage = sweep.Stage,
            TotalExtensions = sweep.TotalExtensions,
            TotalSources = sweep.TotalSources,
            CompletedExtensions = sweep.CompletedExtensions,
            // Attach snapshot: everything the running sweep already found. The client dedupes by
            // mihonId, so overlap with in-flight hub events is harmless.
            Results = sweep.Results.Values.OrderByDescending(r => r.Relevance).ThenBy(r => r.Title).ToList()
        };
    }

    /// <summary>
    /// Cancels an in-flight sweep (retyped query, dialog closed). Worker processes are killed by
    /// the pool when the token fires. Returns false when the id is unknown (already finished).
    /// </summary>
    public bool Cancel(string searchId)
    {
        if (string.IsNullOrEmpty(searchId) || !_byId.TryGetValue(searchId, out Sweep? sweep))
            return false;
        _logger.LogInformation("Discovery sweep {SearchId} cancelled by client.", searchId);
        try { sweep.Cts.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    private async Task RunSweepAsync(Sweep sweep, string keyword, List<string> languages)
    {
        CancellationToken token = sweep.Cts.Token;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DiscoverySearchService>();
            var thumbs = scope.ServiceProvider.GetRequiredService<ThumbCacheService>();

            var stream = new DiscoveryStreamCallbacks
            {
                OnResults = async dtos =>
                {
                    var fresh = new List<DiscoverySeriesDto>();
                    foreach (DiscoverySeriesDto dto in dtos)
                    {
                        if (dto.MihonId != null && sweep.Results.TryAdd(dto.MihonId, dto))
                            fresh.Add(dto);
                    }
                    if (fresh.Count == 0)
                        return;
                    await _hub.Clients.All.SendAsync(HubEventName, new DiscoverySearchEventDto
                    {
                        SearchId = sweep.SearchId,
                        Type = "results",
                        Stage = sweep.Stage,
                        CompletedExtensions = sweep.CompletedExtensions,
                        TotalExtensions = sweep.TotalExtensions,
                        Results = fresh
                    }, token).ConfigureAwait(false);
                },
                OnProgress = async (stage, done, total) =>
                {
                    sweep.Stage = stage;
                    sweep.CompletedExtensions = done; // events may arrive slightly out of order; the client takes the max
                    sweep.TotalExtensions = total;
                    await _hub.Clients.All.SendAsync(HubEventName, new DiscoverySearchEventDto
                    {
                        SearchId = sweep.SearchId,
                        Type = "progress",
                        Stage = stage,
                        CompletedExtensions = done,
                        TotalExtensions = total
                    }, token).ConfigureAwait(false);
                }
            };

            List<DiscoverySeriesDto> final = await service.SearchSeriesAsync(keyword, languages, 0.1f, token, stream).ConfigureAwait(false);
            await thumbs.PopulateThumbsAsync(final, "/api/image/", token).ConfigureAwait(false);
            _memoryCache.Set(CacheKey(sweep.Key), final, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ResultCacheDuration });

            await SendFinalEventAsync(sweep, "completed", final.Count).ConfigureAwait(false);
            _logger.LogInformation("Discovery sweep {SearchId} completed with {Count} results.", sweep.SearchId, final.Count);
        }
        catch (OperationCanceledException)
        {
            // Partial results of a cancelled sweep are discarded, not cached: a fresh identical
            // query later gets a full sweep instead of a silently incomplete cached set.
            await SendFinalEventAsync(sweep, "cancelled", null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discovery sweep {SearchId} failed.", sweep.SearchId);
            await SendFinalEventAsync(sweep, "failed", null).ConfigureAwait(false);
        }
        finally
        {
            _byKey.TryRemove(sweep.Key, out _);
            _byId.TryRemove(sweep.SearchId, out _);
            sweep.Cts.Dispose();
        }
    }

    private async Task SendFinalEventAsync(Sweep sweep, string type, int? totalResults)
    {
        try
        {
            await _hub.Clients.All.SendAsync(HubEventName, new DiscoverySearchEventDto
            {
                SearchId = sweep.SearchId,
                Type = type,
                Stage = sweep.Stage,
                CompletedExtensions = sweep.CompletedExtensions,
                TotalExtensions = sweep.TotalExtensions,
                TotalResults = totalResults
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send final discovery event for sweep {SearchId}.", sweep.SearchId);
        }
    }
}
