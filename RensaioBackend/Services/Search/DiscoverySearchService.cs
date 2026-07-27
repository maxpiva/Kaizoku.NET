using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Import;
using RensaioBackend.Services.Scrobbling;
using RensaioBackend.Services.Settings;
using Microsoft.Extensions.Caching.Memory;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Collections.Concurrent;
using System.Globalization;

namespace RensaioBackend.Services.Search
{
    /// <summary>
    /// Opt-in "search more sources" support: searches extensions the user has NOT installed by
    /// shadow-loading them at the bridge level (download APK + dex2jar + classload) without ever
    /// registering them as installed. No provider DB rows are created, no jobs are scheduled and
    /// the Sources page is completely unaffected — shadow extensions keep showing as "Available".
    /// </summary>
    public class DiscoverySearchService
    {
        private readonly MihonBridgeService _mihon;
        private readonly SettingsService _settings;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<DiscoverySearchService> _logger;

        public DiscoverySearchService(
            MihonBridgeService mihon,
            SettingsService settings,
            IMemoryCache memoryCache,
            ILogger<DiscoverySearchService> logger)
        {
            _mihon = mihon;
            _settings = settings;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        private async Task<List<string>> NormalizeLanguagesAsync(List<string>? languages, CancellationToken token)
        {
            if (languages == null || languages.Count == 0)
            {
                var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                languages = settings.PreferredLanguages.ToList();
            }
            if (languages.Count == 0)
            {
                languages = ["en"];
            }
            return languages;
        }

        private static bool MatchesLanguage(string? language, HashSet<string> languageSet)
            => !string.IsNullOrEmpty(language) && (language == "all" || languageSet.Contains(language));

        /// <summary>
        /// Enumerates online-repository extensions that are NOT installed, filtered by language and
        /// the NSFW visibility setting, deduplicated by package, deterministically ordered and capped
        /// by <see cref="EditableSettingsDto.MaxDiscoverySearchExtensions"/> (MVP guard so a first
        /// request never tries to convert hundreds of APKs).
        /// </summary>
        public async Task<List<(TachiyomiRepository Repository, TachiyomiExtension Extension)>> GetEligibleExtensionsAsync(
            List<string>? languages, CancellationToken token = default)
        {
            var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            languages = await NormalizeLanguagesAsync(languages, token).ConfigureAwait(false);
            var languageSet = new HashSet<string>(languages, StringComparer.InvariantCultureIgnoreCase);

            HashSet<string> installedPackages = _mihon.ListExtensions()
                .Select(g => g.GetActiveEntry().Extension.Package)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool includeNsfw = settings.NsfwVisibility == NsfwVisibility.Show;

            var eligible = new List<(TachiyomiRepository Repository, TachiyomiExtension Extension)>();
            var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TachiyomiRepository repo in _mihon.ListOnlineRepositories().OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (TachiyomiExtension ext in repo.Extensions.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(ext.Package) || installedPackages.Contains(ext.Package) || seenPackages.Contains(ext.Package))
                        continue;
                    if (!includeNsfw && ext.Nsfw == 1)
                        continue;
                    bool languageMatch = MatchesLanguage(ext.Language, languageSet)
                                         || ext.Sources.Any(s => MatchesLanguage(s.Language, languageSet));
                    if (!languageMatch)
                        continue;
                    seenPackages.Add(ext.Package);
                    eligible.Add((repo, ext));
                }
            }

            // <= 0 means unlimited: search every eligible not-installed extension.
            int cap = settings.MaxDiscoverySearchExtensions;
            if (cap > 0 && eligible.Count > cap)
                eligible = eligible.Take(cap).ToList();
            return eligible;
        }

        /// <summary>
        /// Cheap counts for the UI's "Search N more sources" label. Uses the same (capped) eligible
        /// set that a discovery search would actually run against.
        /// </summary>
        public async Task<DiscoverySourcesDto> GetDiscoverySourcesAsync(List<string>? languages, CancellationToken token = default)
        {
            languages = await NormalizeLanguagesAsync(languages, token).ConfigureAwait(false);
            var languageSet = new HashSet<string>(languages, StringComparer.InvariantCultureIgnoreCase);
            var eligible = await GetEligibleExtensionsAsync(languages, token).ConfigureAwait(false);

            int sourceCount = 0;
            foreach ((_, TachiyomiExtension ext) in eligible)
            {
                int matching = ext.Sources.Count(s => MatchesLanguage(s.Language, languageSet));
                if (matching == 0)
                    matching = 1; // matched by the extension's own language; index metadata incomplete
                sourceCount += matching;
            }

            return new DiscoverySourcesDto
            {
                ExtensionCount = eligible.Count,
                SourceCount = sourceCount
            };
        }

        /// <summary>
        /// Runs the discovery search: shadow-loads every eligible not-installed extension (the first
        /// time this downloads + converts the APK, which can be slow) and then runs the same search
        /// call as the normal path, with the same <see cref="SourceTimeout"/> protection applied to
        /// the search call only — never to the download/convert step, so cold conversions are not
        /// cancelled pointlessly.
        /// </summary>
        public async Task<List<DiscoverySeriesDto>> SearchSeriesAsync(string keyword, List<string>? languages,
            double threshold = 0.1f, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return [];

            var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            languages = await NormalizeLanguagesAsync(languages, token).ConfigureAwait(false);
            var languageSet = new HashSet<string>(languages, StringComparer.InvariantCultureIgnoreCase);
            var eligible = await GetEligibleExtensionsAsync(languages, token).ConfigureAwait(false);
            if (eligible.Count == 0)
                return [];

            string cacheKey = "D" + keyword + threshold.ToString(CultureInfo.InvariantCulture) + "_" + string.Join(',', languages) + "_" +
                              string.Join(',', eligible.Select(e => e.Extension.Package));
            if (_memoryCache.TryGetValue(cacheKey, out List<DiscoverySeriesDto>? cachedResult))
            {
                _logger.LogInformation("Returning cached discovery search result for keyword '{keyword}'.", keyword);
                return cachedResult!;
            }

            _logger.LogInformation("Discovery search for '{keyword}' across {Count} not-installed extensions in languages: {langs}",
                keyword, eligible.Count, string.Join(",", languages));

            var sourceInfo = new ConcurrentDictionary<string, (string SourceName, string Package, string? RepoName, string ExtensionName)>();
            var results = new ConcurrentBag<(ParsedManga Manga, string MihonProviderId, string Language)>();
            int maxConcurrency = Math.Min(settings.NumberOfSimultaneousSearches, eligible.Count);

            await Parallel.ForEachAsync(
                eligible,
                new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = token },
                async (candidate, ct) =>
                {
                    (TachiyomiRepository repo, TachiyomiExtension ext) = candidate;
                    IExtensionInterop interop;
                    try
                    {
                        // Shadow-load (download + convert on first use). Intentionally NOT bounded by
                        // SourceTimeout: dex2jar conversions are globally serialized and a cold run may
                        // legitimately take a while.
                        interop = await _mihon.GetDiscoveryInteropAsync(ext, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Skipping discovery extension {Package}: shadow-load failed.", ext.Package);
                        return;
                    }

                    foreach (ISourceInterop src in interop.Sources)
                    {
                        if (!MatchesLanguage(src.Language, languageSet))
                            continue;
                        string mihonProviderId = ext.Package + "|" + src.Id;
                        sourceInfo.TryAdd(mihonProviderId, (src.Name, ext.Package, repo.Name, ext.Name));
                        try
                        {
                            var searchResult = await SourceTimeout
                                .RunAsync(c => src.SearchAsync(1, keyword, c), ct)
                                .ConfigureAwait(false);
                            if (searchResult?.Mangas == null || searchResult.Mangas.Count == 0)
                                continue;
                            var seenUrls = new HashSet<string>();
                            foreach (ParsedManga manga in searchResult.Mangas)
                            {
                                if (seenUrls.Add(manga.Url))
                                    results.Add((manga, mihonProviderId, src.Language));
                            }
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (TimeoutException)
                        {
                            _logger.LogWarning("Discovery search for source {Name} timed out after {Seconds}s; skipping.",
                                src.Name, SourceTimeout.DefaultTimeout.TotalSeconds);
                        }
                        catch (HttpRequestException r)
                        {
                            _logger.LogWarning("Error in discovery search for source {Name}: Http Error {StatusCode}.", src.Name, r.StatusCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in discovery search for source {Name}: {Message}", src.Name, ex.Message);
                        }
                    }
                }).ConfigureAwait(false);

            List<LinkedSeriesDto> linked = results.ToList().FindAndLinkSimilarSeries(threshold);

            var finalResults = new List<DiscoverySeriesDto>();
            foreach (LinkedSeriesDto ls in linked.DistinctBy(a => a.MihonId))
            {
                if (ls.MihonProviderId == null || !sourceInfo.TryGetValue(ls.MihonProviderId, out var info))
                    continue;
                finalResults.Add(new DiscoverySeriesDto
                {
                    MihonId = ls.MihonId,
                    MihonProviderId = ls.MihonProviderId,
                    BridgeItemInfo = ls.BridgeItemInfo,
                    ProviderId = ls.ProviderId,
                    Provider = info.SourceName,
                    Lang = ls.Lang,
                    Title = ls.Title,
                    ThumbnailUrl = ls.ThumbnailUrl,
                    LinkedIds = ls.LinkedIds,
                    UseCover = ls.UseCover,
                    IsStorage = false,
                    IsLocal = false,
                    Installed = false,
                    ExtensionPkg = info.Package,
                    ExtensionRepoName = info.RepoName,
                    ExtensionName = info.ExtensionName
                });
            }

            // Reorder results by fuzzy relevance to the search keyword (same as the normal path).
            if (finalResults.Count > 0)
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

            _memoryCache.Set(cacheKey, finalResults, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) });
            _logger.LogInformation("Discovery search for '{keyword}' returned {Count} results.", keyword, finalResults.Count);
            return finalResults;
        }
    }
}
