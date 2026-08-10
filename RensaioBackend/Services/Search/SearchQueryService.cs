using RensaioBackend.Extensions;
using RensaioBackend.Models.Abstractions;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Images;
using RensaioBackend.Services.Import;
using RensaioBackend.Services.Providers;
using RensaioBackend.Services.Settings;
using Microsoft.Extensions.Caching.Memory;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Collections.Concurrent;
using System.Globalization;

using RensaioBackend.Services.Scrobbling;

namespace RensaioBackend.Services.Search
{
    /// <summary>
    /// Service for search query operations following CQRS pattern
    /// </summary>
    public class SearchQueryService
    {
        private readonly MihonBridgeService _mihon;
        private readonly SettingsService _settings;
        private readonly ProviderCacheService _providerCache;
        private readonly ThumbCacheService _thumb;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<SearchQueryService> _logger;


        public SearchQueryService(
            MihonBridgeService mihon,
            SettingsService settings,
            ProviderCacheService providerCache,
            IMemoryCache memoryCache,
            ThumbCacheService thumb,
            ILogger<SearchQueryService> logger)
        {
            _mihon = mihon;
            _settings = settings;
            _providerCache = providerCache;
            _memoryCache = memoryCache;
            _thumb = thumb;
            _logger = logger;
        }

        /// <summary>
        /// Gets all available search sources based on preferred languages
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of available search sources</returns>
        public async Task<List<SearchSourceDto>> GetAvailableSearchSourcesAsync(CancellationToken token = default)
        {
            var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            var languages = settings.PreferredLanguages.ToList();
            if (languages.Count == 0)
            {
                languages = ["en"]; // Default to English if no languages set
            }

            var summarySources = await _providerCache.GetProviderSummariesForLanguagesAsync(languages, token).ConfigureAwait(false);
            List<SearchSourceDto> views = new List<SearchSourceDto>();
            foreach ((string mihonProviderId, SmallProviderDto summary) in summarySources)
            {
                SearchSourceDto v = new SearchSourceDto
                {
                    Language = summary.Language,
                    Provider = summary.Provider,
                    Scanlator = summary.Scanlator,
                    IsStorage = summary.IsStorage,
                    ThumbnailUrl = summary.ThumbnailUrl,
                    Status = summary.Status,
                    Url = summary.Url,
                    MihonProviderId = mihonProviderId
                };
                views.Add(v);
            }
            return views;
        }


        /// <summary>
        /// Searches for series across multiple sources with language and source filtering
        /// </summary>
        /// <param name="keyword">Search keyword</param>
        /// <param name="languages">List of language codes to search in</param>
        /// <param name="searchSources">Optional list of specific source IDs to search</param>
        /// <param name="threshold">Similarity threshold for linking series</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of linked series matching the search criteria</returns>
        public async Task<List<LinkedSeriesDto>> SearchSeriesAsync(string keyword, List<string> languages,
            List<string>? searchSources = null, double threshold = 0.1f, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(keyword) || languages == null || languages.Count == 0)
            {
                _logger.LogWarning("Search for '{keyword}' skipped: {reason}.", keyword,
                    string.IsNullOrWhiteSpace(keyword) ? "empty keyword" : "no languages provided");
                return [];
            }

            var appSettings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            var filteredSources = await _providerCache.GetSourcesForLanguagesAsync(languages, token).ConfigureAwait(false);

            string langs = languages.Count == 0 ? "all" : string.Join(",", languages);
            if (searchSources!=null && searchSources.Count>0)
                filteredSources = filteredSources.Where(s => searchSources.Contains(s.MihonProviderId)).ToList();
            _logger.LogInformation("Searching for '{keyword}' across {Count} providers in languages: {langs}", keyword, filteredSources.Count, langs);

            return await SearchSeriesAsync(keyword, filteredSources, appSettings, threshold, token).ConfigureAwait(false);
        }

        public async Task<List<LinkedSeriesDto>> SearchSeriesAsync(List<(string keyword, ProviderStorageEntity ps)> sources, SettingsDto? appSettings, double threshold = 0.1f, CancellationToken token = default)
        {
            var results = new ConcurrentBag<(string Keyword, ProviderStorageEntity Storage, MangaList Result)>();
            var maxConcurrency = Math.Min(appSettings?.NumberOfSimultaneousSearches ?? 10, sources.Count);

            await Parallel.ForEachAsync(
                sources,
                new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = token },
                async (source, ct) =>
                {
                    // Try up to a few times for providers that are slow or have temporary issues.
                    // Each attempt is bounded by SourceTimeout so a single stuck provider can't hang
                    // the whole import; a timed-out or cancelled provider is not retried.
                    const int maxAttempts = 3;
                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        try
                        {
                            var src = await _mihon.SourceFromProviderIdAsync(source.ps.MihonProviderId).ConfigureAwait(false);
                            var searchResult = await SourceTimeout
                                .RunAsync(c => src.SearchAsync(1, source.keyword, c), ct)
                                .ConfigureAwait(false);
                            if (searchResult != null && searchResult.Mangas.Count > 0)
                            {
                                // Remove duplicates within the same source
                                var uniqueSeries = new List<ParsedManga>();
                                foreach (var series in searchResult.Mangas)
                                {
                                    if (uniqueSeries.All(a => a.Url != series.Url))
                                        uniqueSeries.Add(series);
                                }

                                searchResult.Mangas = uniqueSeries;
                                results.Add((source.keyword, source.ps, searchResult));
                            }
                            break; // success or empty result – done with this provider
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw; // the job itself was cancelled
                        }
                        catch (TimeoutException)
                        {
                            _logger.LogWarning("Search for provider {Name} timed out after {Seconds}s; skipping.", source.ps.Name, SourceTimeout.DefaultTimeout.TotalSeconds);
                            break; // a stuck provider won't recover by retrying
                        }
                        catch (HttpRequestException r)
                        {
                            _logger.LogWarning("Error searching provider {Name}: Http Error {StatusCode}.", source.ps.Name, r.StatusCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error searching provider {Name}: {Message}", source.ps.Name, ex.Message);
                            break; // unexpected error – don't spin
                        }
                    }

                }).ConfigureAwait(false);


            var allSeries = new List<(ParsedManga Manga, string MihonProiderId, string Language)>();
            foreach (var (pi, providerStorage, result) in results)
            {
                foreach (ParsedManga l in result.Mangas)
                {
                    if (!allSeries.Any(a=>a.Manga.Url == l.Url && a.MihonProiderId == providerStorage.MihonProviderId))
                        allSeries.Add((l, providerStorage.MihonProviderId, providerStorage.Language));
                }
            }

            var linked = allSeries.FindAndLinkSimilarSeries(threshold);


            Dictionary<string, ProviderStorageEntity> sourceDict = new Dictionary<string, ProviderStorageEntity>();
            foreach (var n in sources)
            {
                if (!sourceDict.ContainsKey(n.ps.MihonProviderId))
                    sourceDict.Add(n.ps.MihonProviderId, n.ps);
            }

            // Enrich linked series with provider information
            linked.ForEach(a =>
            {
                a.Provider = sourceDict[a.MihonProviderId!].Name;
                a.IsStorage = sourceDict[a.MihonProviderId!].IsStorage;
            });

            return linked.ToList();

        }


        /// <summary>
        /// Searches for series across specified sources with caching
        /// </summary>
        /// <param name="keyword">Search keyword</param>
        /// <param name="sources">Dictionary of sources to search</param>
        /// <param name="appSettings">Application settings</param>
        /// <param name="threshold">Similarity threshold for linking series</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of linked series matching the search criteria</returns>
        public async Task<List<LinkedSeriesDto>> SearchSeriesAsync(string keyword, List<ProviderStorageEntity> sources,
            SettingsDto? appSettings, double threshold = 0.1f, CancellationToken token = default)
        {
            try
            {
                Dictionary<string, ProviderStorageEntity> sourceDict = sources.ToDictionary(s => s.MihonProviderId, s => s);
                string cacheKey = "S" + keyword + threshold.ToString(CultureInfo.InvariantCulture) + "_" + string.Join(',', sourceDict.Keys);

                if (_memoryCache.TryGetValue(cacheKey, out List<LinkedSeriesDto>? cachedResult))
                {
                    _logger.LogInformation("Returning cached search result for keyword '{keyword}' with threshold {threshold}", keyword, threshold);
                    return cachedResult!;
                }
                // Execute parallel search across sources
                var results = new ConcurrentBag<(string providerId, string lang, MangaList)>();
                var maxConcurrency = Math.Min(appSettings?.NumberOfSimultaneousSearches ?? 10, sources.Count);

                await Parallel.ForEachAsync(
                    sourceDict.Keys,
                    new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = token },
                    async (providerId, ct) =>
                    {
                        try
                        {
                            var src = await _mihon.SourceFromProviderIdAsync(providerId).ConfigureAwait(false);
                            var searchResult = await SourceTimeout
                                .RunAsync(c => src.SearchAsync(1, keyword, c), ct)
                                .ConfigureAwait(false);
                            if (searchResult != null && searchResult.Mangas.Count > 0)
                            {
                                // Remove duplicates within the same source
                                var uniqueSeries = new List<ParsedManga>();
                                foreach (var series in searchResult.Mangas)
                                {
                                    if (uniqueSeries.All(a => a.Url != series.Url))
                                        uniqueSeries.Add(series);
                                }

                                searchResult.Mangas = uniqueSeries;
                                results.Add((providerId, src.Language, searchResult));
                            }
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw; // the job itself was cancelled
                        }
                        catch (TimeoutException)
                        {
                            _logger.LogWarning("Search for provider {Name} timed out after {Seconds}s; skipping.", sourceDict[providerId].Name, SourceTimeout.DefaultTimeout.TotalSeconds);
                        }
                        catch (HttpRequestException r)
                        {
                            _logger.LogWarning("Error searching provider {Name}: Http Error {StatusCode}.", sourceDict[providerId].Name, r.StatusCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error searching provider {Name}: {Message}", sourceDict[providerId].Name, ex.Message);
                        }
                    }).ConfigureAwait(false);

                // Process and link similar series
                var allSeries = new List<(ParsedManga Manga, string ProviderId, string Language)>();
                foreach (var (providerId, lang, result) in results)
                {
                   
                    allSeries.AddRange(result.Mangas.Select(m => (m, providerId, lang)));
                }
                foreach(var n in allSeries)
                {
                    if (!string.IsNullOrEmpty(n.Manga.ThumbnailUrl))
                    {
                        await _thumb.AddUrlAsync(n.Manga.ThumbnailUrl, n.ProviderId, token).ConfigureAwait(false);
                    }
                }
                var linked = allSeries.FindAndLinkSimilarSeries(threshold);


                // Enrich linked series with provider information
                linked.ForEach(a =>
                {
                    a.Provider = sourceDict[a.MihonProviderId!].Name;
                    a.IsStorage = sourceDict[a.MihonProviderId!].IsStorage;
                });

                // Reorder results by fuzzy relevance to the search keyword
                var finalResults = linked.DistinctBy(a => a.MihonId).ToList();

                // Only try to reorder if we have a non-empty keyword
                if (!string.IsNullOrWhiteSpace(keyword) && finalResults.Count > 0)
                {
                    var candidates = finalResults
                        .Where(r => r.MihonId != null)
                        .Select(r => (r.Title, Id: r.MihonId!))
                        .ToList();

                    if (candidates.Count > 0)
                    {
                        var scored = TitleMatcher.MatchTitles(
                            originalTitles: new[] { keyword },
                            candidates: candidates,
                            minimumScore: 0);

                        var scoreLookup = scored.ToDictionary(s => s.Id, s => s.Percentage);

                        finalResults = finalResults
                            .OrderByDescending(r => r.MihonId != null && scoreLookup.TryGetValue(r.MihonId, out var score) ? score : -1)
                            .ThenBy(r => r.Title)
                            .ToList();
                    }
                }

                // Cache results for 30 seconds
                _memoryCache.Set(cacheKey, finalResults, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) });

                return finalResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SearchSeries");
                return [];
            }
        }
    }
}