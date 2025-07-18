using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Extensions;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Providers;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Globalization;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Settings;
using KaizokuBackend.Services.Series;

namespace KaizokuBackend.Services.Search
{
    /// <summary>
    /// Service for search query operations following CQRS pattern
    /// </summary>
    public class SearchQueryService
    {
        private readonly SuwayomiClient _suwayomi;
        private readonly SettingsService _settings;
        private readonly ProviderCacheService _providerCache;
        private readonly IMemoryCache _memoryCache;
        private readonly ContextProvider _baseUrl;
        private readonly ILogger<SearchQueryService> _logger;

        public SearchQueryService(
            SuwayomiClient suwayomi,
            SettingsService settings,
            ProviderCacheService providerCache,
            IMemoryCache memoryCache,
            ContextProvider baseUrl,
            ILogger<SearchQueryService> logger)
        {
            _suwayomi = suwayomi;
            _settings = settings;
            _providerCache = providerCache;
            _memoryCache = memoryCache;
            _baseUrl = baseUrl;
            _logger = logger;
        }

        /// <summary>
        /// Gets all available search sources based on preferred languages
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of available search sources</returns>
        public async Task<List<SearchSource>> GetAvailableSearchSourcesAsync(CancellationToken token = default)
        {
            var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            var languages = settings.PreferredLanguages.ToList();
            if (languages.Count == 0)
            {
                languages = ["en"]; // Default to English if no languages set
            }
            
            var allSources = await _providerCache.GetSourcesForLanguagesAsync(languages, token).ConfigureAwait(false);
            return allSources.Keys.Select(a => new SearchSource
            {
                SourceId = a.Id.ToString(),
                SourceName = a.Name,
                Language = a.Lang.ToLowerInvariant(),
            }).ToList();
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
        public async Task<List<LinkedSeries>> SearchSeriesAsync(string keyword, List<string> languages,
            List<string>? searchSources = null, double threshold = 0.1f, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(keyword) || languages == null || languages.Count == 0)
            {
                return [];
            }

            var appSettings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            var filteredSources = await _providerCache.GetSourcesForLanguagesAsync(languages, token).ConfigureAwait(false);

            if (searchSources != null && searchSources.Count != 0)
            {
                filteredSources = filteredSources.Where(a => searchSources.Contains(a.Key.Id.ToString())).ToDictionary(a => a.Key, a => a.Value);
            }

            string langs = languages.Count == 0 ? "all" : string.Join(",", languages);
            _logger.LogInformation("Searching for '{keyword}' across {Count} providers in languages: {langs}", keyword, filteredSources.Count, langs);
            
            return await SearchSeriesAsync(keyword, filteredSources, appSettings, threshold, token).ConfigureAwait(false);
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
        public async Task<List<LinkedSeries>> SearchSeriesAsync(string keyword, Dictionary<SuwayomiSource, ProviderStorage> sources,
            KaizokuBackend.Models.Settings? appSettings, double threshold = 0.1f, CancellationToken token = default)
        {
            try
            {
                // Check cache first
                string cacheKey = "S" + keyword + threshold.ToString(CultureInfo.InvariantCulture) + "_" + string.Join(',', sources.Keys.Select(a => a.Id));
                
                if (_memoryCache.TryGetValue(cacheKey, out List<LinkedSeries>? cachedResult))
                {
                    _logger.LogInformation("Returning cached search result for keyword '{keyword}' with threshold {threshold}", keyword, threshold);
                    return cachedResult!;
                }

                // Execute parallel search across sources
                var results = new ConcurrentBag<(SuwayomiSource Source, SuwayomiSeriesResult Result)>();
                var maxConcurrency = Math.Min(appSettings?.NumberOfSimultaneousSearches ?? 10, sources.Count);

                await Parallel.ForEachAsync(
                    sources.Keys,
                    new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = token },
                    async (source, ct) =>
                    {
                        try
                        {
                            var searchResult = await _suwayomi.SearchSeriesAsync(source.Id, keyword, 1, ct).ConfigureAwait(false);
                            if (searchResult != null && searchResult.MangaList.Count > 0)
                            {
                                // Remove duplicates within the same source
                                var uniqueSeries = new List<SuwayomiSeries>();
                                foreach (var series in searchResult.MangaList)
                                {
                                    if (uniqueSeries.All(a => a.Id != series.Id))
                                        uniqueSeries.Add(series);
                                }

                                searchResult.MangaList = uniqueSeries;
                                results.Add((source, searchResult));
                            }
                        }
                        catch (HttpRequestException r)
                        {
                            _logger.LogWarning("Error searching provider {Name}: Http Error {StatusCode}.", source.Name, r.StatusCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error searching provider {Name}: {Message}", source.Name, ex.Message);
                        }
                    }).ConfigureAwait(false);

                // Process and link similar series
                var allSeries = new List<SuwayomiSeries>();
                foreach (var (source, result) in results)
                {
                    allSeries.AddRange(result.MangaList);
                }

                var linked = allSeries.FindAndLinkSimilarSeries(_baseUrl, threshold);
                var sourcesDict = sources.Keys.ToDictionary(a => a.Id, a => a);
                
                // Enrich linked series with provider information
                linked.ForEach(a =>
                {
                    a.Provider = sourcesDict[a.ProviderId].Name;
                    a.Lang = sourcesDict[a.ProviderId].Lang.ToLowerInvariant();
                    a.IsStorage = sources[sourcesDict[a.ProviderId]].IsStorage;
                });

                var finalResults = linked.DistinctBy(a => a.Id).OrderByLevenshteinDistance(a => a.Title, keyword).ToList();
                
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