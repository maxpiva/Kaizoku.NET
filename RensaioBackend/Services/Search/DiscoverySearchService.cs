using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Import;
using RensaioBackend.Services.Scrobbling;
using RensaioBackend.Services.Search.Discovery;
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
        /// <summary>
        /// Extensions that crashed a discovery worker twice (once in a shared batch, once alone).
        /// Skipped for the rest of the process lifetime so one broken extension cannot keep
        /// killing workers on every search.
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> BadExtensions = new(StringComparer.OrdinalIgnoreCase);

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
                    if (BadExtensions.ContainsKey(ext.Package))
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

            int cap = Math.Max(1, settings.MaxDiscoverySearchExtensions);
            if (eligible.Count > cap)
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

            bool searched = false;
            if (settings.DiscoverySearchWorkersEnabled)
            {
                try
                {
                    searched = await SearchViaWorkersAsync(keyword, languageSet, eligible, settings, maxConcurrency,
                        sourceInfo, results, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Discovery worker execution failed; falling back to the in-process search path.");
                    searched = false;
                }
            }
            if (!searched)
            {
                await SearchInProcessAsync(keyword, languageSet, eligible, maxConcurrency, sourceInfo, results, token).ConfigureAwait(false);
            }

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

        /// <summary>
        /// Legacy in-process path: shadow-loads every eligible extension into THIS process and
        /// searches it. Kept as the fallback when workers are disabled or unavailable. Note that
        /// classloaded JARs can never be unloaded, so memory grows with each new extension loaded.
        /// </summary>
        private async Task SearchInProcessAsync(string keyword, HashSet<string> languageSet,
            List<(TachiyomiRepository Repository, TachiyomiExtension Extension)> eligible, int maxConcurrency,
            ConcurrentDictionary<string, (string SourceName, string Package, string? RepoName, string ExtensionName)> sourceInfo,
            ConcurrentBag<(ParsedManga Manga, string MihonProviderId, string Language)> results,
            CancellationToken token)
        {
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
                            if (searchResult == null || searchResult.Mangas.Count == 0)
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
        }

        /// <summary>
        /// Worker-process path. Phase 1 prepares the disk artifacts (APK download + dex2jar) in THIS
        /// process — conversion stays globally serialized here and cached artifacts are shared. Phase 2
        /// fans the prepared extensions out to short-lived worker processes (batch size doubles as the
        /// recycle-after-N bound, worker count is capped) that classload and search, streaming results
        /// back over stdout. A crashed worker's begun-but-unfinished extensions get one solo retry;
        /// crashing alone marks the extension bad for the process lifetime. Returns false when no
        /// worker executable could be resolved so the caller can fall back in-process.
        /// </summary>
        private async Task<bool> SearchViaWorkersAsync(string keyword, HashSet<string> languageSet,
            List<(TachiyomiRepository Repository, TachiyomiExtension Extension)> eligible, EditableSettingsDto settings, int maxConcurrency,
            ConcurrentDictionary<string, (string SourceName, string Package, string? RepoName, string ExtensionName)> sourceInfo,
            ConcurrentBag<(ParsedManga Manga, string MihonProviderId, string Language)> results,
            CancellationToken token)
        {
            (string FileName, string? DllPath)? launch = DiscoveryWorkerPool.ResolveWorkerLaunch();
            if (launch == null)
            {
                _logger.LogWarning("No discovery worker executable found next to the backend; using the in-process search path.");
                return false;
            }

            var infoByPackage = new Dictionary<string, (string? RepoName, string ExtensionName)>(StringComparer.OrdinalIgnoreCase);
            foreach ((TachiyomiRepository repo, TachiyomiExtension ext) in eligible)
                infoByPackage[ext.Package] = (repo.Name, ext.Name);

            // Phase 1: prepare artifacts (download + convert) without classloading anything here.
            var prepared = new ConcurrentBag<DiscoveryWorkerExtension>();
            await Parallel.ForEachAsync(
                eligible,
                new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = token },
                async (candidate, ct) =>
                {
                    try
                    {
                        DiscoveryArtifact artifact = await _mihon.PrepareDiscoveryArtifactsAsync(candidate.Extension, ct).ConfigureAwait(false);
                        prepared.Add(new DiscoveryWorkerExtension { Entry = artifact.Entry, Folder = artifact.Folder });
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Skipping discovery extension {Package}: artifact preparation failed.", candidate.Extension.Package);
                    }
                }).ConfigureAwait(false);
            if (prepared.IsEmpty)
                return true;

            Preferences preferences = await _mihon.GetPreferencesAsync(token).ConfigureAwait(false);
            int batchSize = Math.Max(1, settings.DiscoveryWorkerBatchSize);
            int maxWorkers = Math.Max(1, settings.MaxDiscoveryWorkers);
            int parallelInWorker = Math.Clamp(settings.NumberOfSimultaneousSearches / maxWorkers, 1, 4);
            TimeSpan inactivityTimeout = SourceTimeout.DefaultTimeout + TimeSpan.FromSeconds(90);
            string scratchRoot = Path.Combine(Path.GetTempPath(), "rensaio-discovery-workers");
            var slots = new ConcurrentStack<int>(Enumerable.Range(0, maxWorkers));
            using var semaphore = new SemaphoreSlim(maxWorkers);

            void OnSourceResult(DiscoveryWorkerEvent evt)
            {
                if (evt.Package == null || evt.SourceId == null || evt.Mangas == null)
                    return;
                string mihonProviderId = evt.Package + "|" + evt.SourceId;
                infoByPackage.TryGetValue(evt.Package, out (string? RepoName, string ExtensionName) info);
                sourceInfo.TryAdd(mihonProviderId, (evt.SourceName ?? evt.Package, evt.Package, info.RepoName, info.ExtensionName ?? evt.Package));
                var seenUrls = new HashSet<string>();
                foreach (ParsedManga manga in evt.Mangas)
                {
                    if (seenUrls.Add(manga.Url))
                        results.Add((manga, mihonProviderId, evt.SourceLanguage ?? ""));
                }
            }

            async Task<DiscoveryWorkerBatchOutcome> RunBatchAsync(List<DiscoveryWorkerExtension> batch, int parallelism)
            {
                await semaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!slots.TryPop(out int slot))
                        slot = 0;
                    try
                    {
                        var input = new DiscoveryWorkerInput
                        {
                            ScratchFolder = Path.Combine(scratchRoot, $"slot{slot}"),
                            Preferences = preferences,
                            Query = keyword,
                            Languages = languageSet.ToList(),
                            SearchTimeoutSeconds = SourceTimeout.DefaultTimeout.TotalSeconds,
                            MaxParallelExtensions = parallelism,
                            Extensions = batch
                        };
                        return await DiscoveryWorkerPool.RunWorkerAsync(launch.Value, input, inactivityTimeout,
                            OnSourceResult, _logger, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        slots.Push(slot);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }

            // Round 1: normal batches. Each worker exits after its batch (recycle-after-N).
            List<List<DiscoveryWorkerExtension>> batches = prepared.Chunk(batchSize).Select(c => c.ToList()).ToList();
            _logger.LogInformation("Discovery search fanning out {Extensions} extensions to {Batches} worker batches (max {Workers} concurrent).",
                prepared.Count, batches.Count, maxWorkers);
            var suspectRetries = new ConcurrentBag<DiscoveryWorkerExtension>();
            var untouchedRetries = new ConcurrentBag<DiscoveryWorkerExtension>();
            await Task.WhenAll(batches.Select(async batch =>
            {
                DiscoveryWorkerBatchOutcome outcome = await RunBatchAsync(batch, parallelInWorker).ConfigureAwait(false);
                if (outcome.CleanExit)
                    return;
                foreach (DiscoveryWorkerExtension item in batch)
                {
                    string package = item.Entry.Extension.Package;
                    if (outcome.Completed.Contains(package) || outcome.FailedManaged.Contains(package))
                        continue;
                    if (outcome.Suspects.Contains(package))
                        suspectRetries.Add(item);
                    else
                        untouchedRetries.Add(item);
                }
            })).ConfigureAwait(false);

            // Round 2: crash suspects run alone (parallelism 1) so blame is unambiguous; extensions
            // the dead worker never started are re-batched normally. No third round — anything that
            // still fails is logged and skipped, and a solo crasher is marked bad for this process.
            var retryBatches = new List<(List<DiscoveryWorkerExtension> Batch, bool Solo)>();
            foreach (DiscoveryWorkerExtension suspect in suspectRetries)
                retryBatches.Add(([suspect], true));
            retryBatches.AddRange(untouchedRetries.Chunk(batchSize).Select(c => (c.ToList(), false)));
            if (retryBatches.Count > 0)
            {
                _logger.LogInformation("Retrying {Suspects} crash suspects solo and {Untouched} unprocessed extensions after worker failures.",
                    suspectRetries.Count, untouchedRetries.Count);
                await Task.WhenAll(retryBatches.Select(async retry =>
                {
                    DiscoveryWorkerBatchOutcome outcome = await RunBatchAsync(retry.Batch, retry.Solo ? 1 : parallelInWorker).ConfigureAwait(false);
                    if (outcome.CleanExit)
                        return;
                    foreach (DiscoveryWorkerExtension item in retry.Batch)
                    {
                        string package = item.Entry.Extension.Package;
                        if (outcome.Completed.Contains(package) || outcome.FailedManaged.Contains(package))
                            continue;
                        if (retry.Solo && outcome.Suspects.Contains(package))
                        {
                            BadExtensions.TryAdd(package, 0);
                            _logger.LogWarning("Discovery extension {Package} crashed its worker twice; marking it bad and skipping it from now on.", package);
                        }
                        else
                        {
                            _logger.LogWarning("Discovery extension {Package} was not searched after repeated worker failures; skipping for this search.", package);
                        }
                    }
                })).ConfigureAwait(false);
            }
            return true;
        }
    }
}
