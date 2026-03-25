using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Images;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Settings;
using Microsoft.Extensions.Caching.Memory;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Collections.Concurrent;
using System.Globalization;

namespace KaizokuBackend.Services.Search
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

            // Overall search timeout — prevents the entire operation from running unbounded
            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            overallCts.CancelAfter(TimeSpan.FromSeconds(60));
            var overallToken = overallCts.Token;

            await Parallel.ForEachAsync(
                sources,
                new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = overallToken },
                async (source, ct) =>
                {
                    // Bail out early if the caller was already cancelled
                    ct.ThrowIfCancellationRequested();

                    // Try up to 3 times for providers that have temporary issues
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            // Per-provider timeout: 15 seconds covering init + search
                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

                            var src = await _mihon.SourceFromProviderIdAsync(source.ps.MihonProviderId, timeoutCts.Token).ConfigureAwait(false);
                            var searchResult = await src.SearchAsync(1, source.keyword, timeoutCts.Token).ConfigureAwait(false);
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
                                break;
                            }
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            _logger.LogWarning("Provider {Name} timed out on attempt {Attempt}/3.", source.ps.Name, attempt + 1);
                        }
                        catch (HttpRequestException r)
                        {
                            _logger.LogWarning("Error searching provider {Name} (attempt {Attempt}/3): Http Error {StatusCode}.", source.ps.Name, attempt + 1, r.StatusCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error searching provider {Name} (attempt {Attempt}/3): {Message}", source.ps.Name, attempt + 1, ex.Message);
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

                // Overall search timeout — prevents the entire operation from running unbounded
                using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                overallCts.CancelAfter(TimeSpan.FromSeconds(60));
                var overallToken = overallCts.Token;

                await Parallel.ForEachAsync(
                    sourceDict.Keys,
                    new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = overallToken },
                    async (providerId, ct) =>
                    {
                        // Bail out early if the caller (e.g. HTTP request) was already cancelled
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            // Per-provider timeout: 15 seconds covering init + search
                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

                            var src = await _mihon.SourceFromProviderIdAsync(providerId, timeoutCts.Token).ConfigureAwait(false);
                            var searchResult = await src.SearchAsync(1, keyword, timeoutCts.Token).ConfigureAwait(false);
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
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            _logger.LogWarning("Provider {Name} timed out after 15s, skipping.", sourceDict[providerId].Name);
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
                // Batch-register all thumbnail URLs in a single DB round-trip
                var thumbUrls = allSeries
                    .Where(n => !string.IsNullOrEmpty(n.Manga.ThumbnailUrl))
                    .Select(n => (n.Manga.ThumbnailUrl, (string?)n.ProviderId))
                    .ToList();
                await _thumb.AddUrlsBatchAsync(thumbUrls, token).ConfigureAwait(false);
                var linked = allSeries.FindAndLinkSimilarSeries(threshold);


                // Enrich linked series with provider information
                linked.ForEach(a =>
                {
                    a.Provider = sourceDict[a.MihonProviderId!].Name;
                    a.IsStorage = sourceDict[a.MihonProviderId!].IsStorage;
                });

                var finalResults = linked.DistinctBy(a => a.MihonId).OrderByLevenshteinDistance(a => a.Title, keyword).ToList();

                // Cache results for 5 minutes — search results don't go stale quickly
                _memoryCache.Set(cacheKey, finalResults, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });

                return finalResults;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Search for '{keyword}' was cancelled (client disconnect or overall timeout).", keyword);
                return [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SearchSeries");
                return [];
            }
        }
    }
}