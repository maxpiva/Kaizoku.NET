using RensaioBackend.Extensions;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Images;
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
        private readonly ThumbCacheService _thumb;
        private readonly DiscoveryWorkerPool _pool;
        private readonly DiscoverySourceHeaderRegistry _headerRegistry;
        private readonly ILogger<DiscoverySearchService> _logger;

        public DiscoverySearchService(
            MihonBridgeService mihon,
            SettingsService settings,
            IMemoryCache memoryCache,
            ThumbCacheService thumb,
            DiscoveryWorkerPool pool,
            DiscoverySourceHeaderRegistry headerRegistry,
            ILogger<DiscoverySearchService> logger)
        {
            _mihon = mihon;
            _settings = settings;
            _memoryCache = memoryCache;
            _thumb = thumb;
            _pool = pool;
            _headerRegistry = headerRegistry;
            _logger = logger;
        }

        public async Task<List<string>> NormalizeLanguagesAsync(List<string>? languages, CancellationToken token)
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
            double threshold = 0.1f, CancellationToken token = default, DiscoveryStreamCallbacks? stream = null)
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

            // Streaming plumbing: convert each per-source batch into ready-to-render DTOs and push
            // them to the caller as they arrive. DTO building touches scoped services (thumb cache /
            // DbContext), so it is serialized behind a gate — batches can arrive from concurrent
            // worker readers.
            StreamHooks? hooks = null;
            if (stream != null)
            {
                int totalExtensions = eligible.Count;
                int preparedExtensions = 0;
                int searchedExtensions = 0;
                var streamGate = new SemaphoreSlim(1, 1);

                async Task EmitProgressAsync(string stage, int done)
                {
                    if (stream.OnProgress == null)
                        return;
                    try
                    {
                        await stream.OnProgress(stage, done, totalExtensions).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Discovery progress callback failed; continuing search.");
                    }
                }

                hooks = new StreamHooks
                {
                    EmitBatch = async batch =>
                    {
                        if (stream.OnResults == null || batch.Count == 0)
                            return;
                        await streamGate.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            List<DiscoverySeriesDto> dtos = await BuildStreamedDtosAsync(keyword, batch, sourceInfo, token).ConfigureAwait(false);
                            if (dtos.Count > 0)
                                await stream.OnResults(dtos).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Discovery result streaming failed for one batch; continuing search.");
                        }
                        finally
                        {
                            streamGate.Release();
                        }
                    },
                    OnPrepared = () => EmitProgressAsync(DiscoveryStreamCallbacks.StagePreparing, Interlocked.Increment(ref preparedExtensions)),
                    OnSearched = () => EmitProgressAsync(DiscoveryStreamCallbacks.StageSearching, Interlocked.Increment(ref searchedExtensions))
                };
            }

            bool searched = false;
            if (settings.DiscoverySearchWorkersEnabled)
            {
                try
                {
                    searched = await SearchViaWorkersAsync(keyword, languageSet, eligible, settings, maxConcurrency,
                        sourceInfo, results, hooks, token).ConfigureAwait(false);
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
                await SearchInProcessAsync(keyword, languageSet, eligible, maxConcurrency, sourceInfo, results, hooks, token).ConfigureAwait(false);
            }

            // Register every thumb URL with its provider id (same as the normal search path) so the
            // image cache can fall back to the already shadow-loaded discovery interop when a plain
            // HTTP fetch is rejected (referer/Cloudflare/CDN header checks).
            foreach (var (manga, mihonProviderId, _) in results)
            {
                if (!string.IsNullOrEmpty(manga.ThumbnailUrl))
                {
                    await _thumb.AddUrlAsync(manga.ThumbnailUrl, mihonProviderId, token).ConfigureAwait(false);
                }
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
                    finalResults.ForEach(r =>
                        r.Relevance = r.MihonId != null && scoreLookup.TryGetValue(r.MihonId, out var score) ? score : 0);
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
            StreamHooks? hooks,
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
                        if (hooks?.OnSearched != null)
                            await hooks.OnSearched().ConfigureAwait(false);
                        return;
                    }

                    foreach (ISourceInterop src in interop.Sources)
                    {
                        if (!MatchesLanguage(src.Language, languageSet))
                            continue;
                        string mihonProviderId = ext.Package + "|" + src.Id;
                        sourceInfo.TryAdd(mihonProviderId, (src.Name, ext.Package, repo.Name, ext.Name));
                        try { _headerRegistry.Register(mihonProviderId, src.GetImageRequestHeaders()); } catch { }
                        try
                        {
                            var searchResult = await SourceTimeout
                                .RunAsync(c => src.SearchAsync(1, keyword, c), ct)
                                .ConfigureAwait(false);
                            if (searchResult?.Mangas == null || searchResult.Mangas.Count == 0)
                                continue;
                            var seenUrls = new HashSet<string>();
                            var fresh = new List<(ParsedManga Manga, string MihonProviderId, string Language)>();
                            foreach (ParsedManga manga in searchResult.Mangas)
                            {
                                if (seenUrls.Add(manga.Url))
                                {
                                    results.Add((manga, mihonProviderId, src.Language));
                                    fresh.Add((manga, mihonProviderId, src.Language));
                                }
                            }
                            if (hooks?.EmitBatch != null && fresh.Count > 0)
                                await hooks.EmitBatch(fresh).ConfigureAwait(false);
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
                    if (hooks?.OnSearched != null)
                        await hooks.OnSearched().ConfigureAwait(false);
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
            StreamHooks? hooks,
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
                    if (hooks?.OnPrepared != null)
                        await hooks.OnPrepared().ConfigureAwait(false);
                }).ConfigureAwait(false);
            if (prepared.IsEmpty)
                return true;

            Preferences preferences = await _mihon.GetPreferencesAsync(token).ConfigureAwait(false);
            int batchSize = Math.Max(1, settings.DiscoveryWorkerBatchSize);
            int maxWorkers = Math.Max(1, settings.MaxDiscoveryWorkers);
            // Per-worker search concurrency. Deliberately NOT divided by the worker count (the old
            // formula made total in-flight searches invariant in maxWorkers — adding workers halved
            // per-worker parallelism and sweeps didn't speed up). Each worker runs up to
            // NumberOfSimultaneousSearches concurrent extension searches, capped by its batch size,
            // so total concurrency = workers x this and the worker knob actually scales the sweep.
            int parallelInWorker = Math.Clamp(settings.NumberOfSimultaneousSearches, 1, batchSize);
            var context = new DiscoveryWorkerContext
            {
                Launch = launch.Value,
                Preferences = preferences,
                InactivityTimeout = SourceTimeout.DefaultTimeout + TimeSpan.FromSeconds(90),
                BatchSize = batchSize,
                WarmPoolEnabled = settings.DiscoveryWarmPoolEnabled,
                IdleTimeout = settings.DiscoveryWorkerIdleTimeout
            };
            _pool.Configure(context);
            using var semaphore = new SemaphoreSlim(maxWorkers);

            async Task OnWorkerEventAsync(DiscoveryWorkerEvent evt)
            {
                if (evt.Type == DiscoveryWorkerEventTypes.ExtensionDone || evt.Type == DiscoveryWorkerEventTypes.ExtensionFailed)
                {
                    if (hooks?.OnSearched != null)
                        await hooks.OnSearched().ConfigureAwait(false);
                    return;
                }
                if (evt.Package == null || evt.SourceId == null || evt.Mangas == null)
                    return;
                string mihonProviderId = evt.Package + "|" + evt.SourceId;
                infoByPackage.TryGetValue(evt.Package, out (string? RepoName, string ExtensionName) info);
                sourceInfo.TryAdd(mihonProviderId, (evt.SourceName ?? evt.Package, evt.Package, info.RepoName, info.ExtensionName ?? evt.Package));
                // Remember the source's own image-request headers so cover fetches can replay
                // them when the plain request is rejected (no interop exists in this process).
                _headerRegistry.Register(mihonProviderId, evt.Headers);
                var seenUrls = new HashSet<string>();
                var fresh = new List<(ParsedManga Manga, string MihonProviderId, string Language)>();
                foreach (ParsedManga manga in evt.Mangas)
                {
                    if (seenUrls.Add(manga.Url))
                    {
                        results.Add((manga, mihonProviderId, evt.SourceLanguage ?? ""));
                        fresh.Add((manga, mihonProviderId, evt.SourceLanguage ?? ""));
                    }
                }
                if (hooks?.EmitBatch != null && fresh.Count > 0)
                    await hooks.EmitBatch(fresh).ConfigureAwait(false);
            }

            async Task<DiscoveryWorkerBatchOutcome> RunBatchAsync(List<DiscoveryWorkerExtension> batch, int parallelism)
            {
                await semaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    return await _pool.RunSearchBatchAsync(batch, keyword, languageSet.ToList(),
                        SourceTimeout.DefaultTimeout.TotalSeconds, parallelism, context,
                        OnWorkerEventAsync, token).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            }

            // Round 1: batches aligned to the warm pool — each warm worker gets the extensions it
            // already has loaded; only the remainder spawns fresh workers.
            List<List<DiscoveryWorkerExtension>> batches = _pool.PlanBatches(prepared.ToList(), batchSize);
            int warmCount = _pool.CountWarm(prepared);
            _logger.LogInformation("Discovery search fanning out {Extensions} extensions ({Warm} warm) to {Batches} worker batches (max {Workers} concurrent).",
                prepared.Count, warmCount, batches.Count, maxWorkers);
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

        /// <summary>
        /// Internal streaming hooks threaded through the two search paths.
        /// </summary>
        private sealed class StreamHooks
        {
            /// <summary>One per-source batch of raw results arrived.</summary>
            public Func<List<(ParsedManga Manga, string MihonProviderId, string Language)>, Task>? EmitBatch { get; init; }
            /// <summary>One extension's artifacts finished preparing (or failed to).</summary>
            public Func<Task>? OnPrepared { get; init; }
            /// <summary>One extension finished searching (done or failed).</summary>
            public Func<Task>? OnSearched { get; init; }
        }

        /// <summary>
        /// Converts one raw per-source batch into ready-to-render DTOs: bridge item info filled,
        /// relevance scored against the keyword and thumbnails registered + rewritten to the local
        /// image cache, so the client can drop them straight into the results list.
        /// </summary>
        private async Task<List<DiscoverySeriesDto>> BuildStreamedDtosAsync(string keyword,
            List<(ParsedManga Manga, string MihonProviderId, string Language)> batch,
            ConcurrentDictionary<string, (string SourceName, string Package, string? RepoName, string ExtensionName)> sourceInfo,
            CancellationToken token)
        {
            var dtos = new List<DiscoverySeriesDto>();
            foreach ((ParsedManga manga, string mihonProviderId, string language) in batch)
            {
                if (string.IsNullOrWhiteSpace(manga.Title) || !sourceInfo.TryGetValue(mihonProviderId, out var info))
                    continue;
                string id = mihonProviderId + "|" + manga.Url;
                var dto = new DiscoverySeriesDto
                {
                    MihonId = id,
                    MihonProviderId = mihonProviderId,
                    Provider = info.SourceName,
                    Lang = language == "all" ? string.Empty : language,
                    Title = manga.Title,
                    ThumbnailUrl = manga.ThumbnailUrl,
                    LinkedIds = [id],
                    IsStorage = false,
                    IsLocal = false,
                    Installed = false,
                    ExtensionPkg = info.Package,
                    ExtensionRepoName = info.RepoName,
                    ExtensionName = info.ExtensionName
                };
                manga.FillBridgeItemInfo(dto);
                dtos.Add(dto);
                if (!string.IsNullOrEmpty(manga.ThumbnailUrl))
                    await _thumb.AddUrlAsync(manga.ThumbnailUrl, mihonProviderId, token).ConfigureAwait(false);
            }
            if (dtos.Count > 0)
            {
                var scored = TitleMatcher.MatchTitles(
                    originalTitles: new[] { keyword },
                    candidates: dtos.Select(d => (d.Title, Id: d.MihonId!)).ToList(),
                    minimumScore: 0);
                var scoreLookup = scored.ToDictionary(s => s.Id, s => s.Percentage);
                foreach (DiscoverySeriesDto dto in dtos)
                {
                    if (dto.MihonId != null && scoreLookup.TryGetValue(dto.MihonId, out int pct))
                        dto.Relevance = pct;
                }
                await _thumb.PopulateThumbsAsync(dtos, "/api/image/", token).ConfigureAwait(false);
            }
            return dtos;
        }

        /// <summary>
        /// Fetches chapter count + status for one discovery result. Prefers the warm worker pool
        /// (the extension is usually still loaded from the sweep that produced the result); when
        /// workers are unavailable it falls back to an in-process shadow-load. Returns null on any
        /// failure — details are a progressive enhancement, never a hard dependency.
        /// </summary>
        public async Task<(int? ChapterCount, int? Status)?> GetDiscoveryDetailsAsync(DiscoverySeriesDto dto, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(dto.MihonProviderId) || string.IsNullOrEmpty(dto.BridgeItemInfo))
            {
                _logger.LogWarning("Discovery details skipped for '{Title}': missing provider id or bridge item info.", dto.Title);
                return null;
            }
            string[] split = dto.MihonProviderId.Split('|');
            if (split.Length < 2 || !long.TryParse(split[1], out long sourceId))
            {
                _logger.LogWarning("Discovery details skipped for '{Title}': unparsable provider id '{ProviderId}'.", dto.Title, dto.MihonProviderId);
                return null;
            }
            string package = split[0];
            var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            TachiyomiExtension? ext = _mihon.ListOnlineRepositories()
                .SelectMany(r => r.Extensions)
                .FirstOrDefault(e => package.Equals(e.Package, StringComparison.OrdinalIgnoreCase));
            if (ext == null)
            {
                _logger.LogWarning("Discovery details skipped for '{Title}': package {Package} no longer in the online repositories.", dto.Title, package);
                return null;
            }
            try
            {
                if (settings.DiscoverySearchWorkersEnabled)
                {
                    (string FileName, string? DllPath)? launch = DiscoveryWorkerPool.ResolveWorkerLaunch();
                    if (launch != null)
                    {
                        DiscoveryArtifact artifact = await _mihon.PrepareDiscoveryArtifactsAsync(ext, token).ConfigureAwait(false);
                        DiscoveryWorkerContext context = await BuildWorkerContextAsync(settings, launch.Value, token).ConfigureAwait(false);
                        DiscoveryWorkerEvent? evt = await _pool.RunDetailsAsync(
                            new DiscoveryWorkerExtension { Entry = artifact.Entry, Folder = artifact.Folder },
                            sourceId, dto.BridgeItemInfo, SourceTimeout.DefaultTimeout.TotalSeconds, context, token).ConfigureAwait(false);
                        return evt == null ? null : (evt.ChapterCount, evt.MangaStatus);
                    }
                }
                // In-process fallback: loads the extension into this process (memory cost noted).
                IExtensionInterop interop = await _mihon.GetDiscoveryInteropAsync(ext, token).ConfigureAwait(false);
                ISourceInterop? src = interop.Sources.FirstOrDefault(s => s.Id == sourceId);
                if (src == null)
                    return null;
                var manga = System.Text.Json.JsonSerializer.Deserialize<Manga>(dto.BridgeItemInfo);
                if (manga == null)
                    return null;
                var update = await SourceTimeout
                    .RunAsync(c => src.GetDetailsAndChaptersAsync(manga, c), token)
                    .ConfigureAwait(false);
                return (update?.Chapters?.Count, update?.Manga != null ? (int)update.Manga.Status : null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discovery details fetch failed for {ProviderId}.", dto.MihonProviderId);
                return null;
            }
        }

        private async Task<DiscoveryWorkerContext> BuildWorkerContextAsync(EditableSettingsDto settings,
            (string FileName, string? DllPath) launch, CancellationToken token)
        {
            Preferences preferences = await _mihon.GetPreferencesAsync(token).ConfigureAwait(false);
            return new DiscoveryWorkerContext
            {
                Launch = launch,
                Preferences = preferences,
                InactivityTimeout = SourceTimeout.DefaultTimeout + TimeSpan.FromSeconds(90),
                BatchSize = Math.Max(1, settings.DiscoveryWorkerBatchSize),
                WarmPoolEnabled = settings.DiscoveryWarmPoolEnabled,
                IdleTimeout = settings.DiscoveryWorkerIdleTimeout
            };
        }

        /// <summary>
        /// Fingerprint of the eligible set the last fully successful precache run covered, so the
        /// recurring job is a no-op (no per-file hashing) while nothing changed.
        /// </summary>
        private static string? _lastPrecacheFingerprint;

        /// <summary>
        /// Pre-converts the discovery artifacts (APK download + dex2jar, no classloading) for every
        /// eligible not-installed extension, so the first automatic discovery search of the day never
        /// pays the cold conversion cost. Runs sequentially — dex2jar is globally serialized anyway,
        /// so this self-throttles and stays out of the way of interactive searches.
        /// </summary>
        public async Task<int> PrepareEligibleArtifactsAsync(CancellationToken token = default)
        {
            var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (!settings.DiscoveryIncludeInSearch || !settings.DiscoveryPrecacheEnabled)
                return 0;

            var eligible = await GetEligibleExtensionsAsync(null, token).ConfigureAwait(false);
            string fingerprint = string.Join('|', eligible.Select(e => e.Extension.Package + ":" + e.Extension.Version).OrderBy(a => a));
            if (fingerprint == _lastPrecacheFingerprint)
            {
                _logger.LogInformation("Discovery precache: eligible set unchanged ({Count} extensions); nothing to do.", eligible.Count);
                return 0;
            }

            _logger.LogInformation("Discovery precache: preparing artifacts for {Count} eligible extensions.", eligible.Count);
            int prepared = 0;
            int failed = 0;
            foreach ((_, TachiyomiExtension ext) in eligible)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await _mihon.PrepareDiscoveryArtifactsAsync(ext, token).ConfigureAwait(false);
                    prepared++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Discovery precache: artifact preparation failed for {Package}.", ext.Package);
                }
            }
            // Only remember a fully clean sweep so failed extensions are retried on the next run.
            if (failed == 0)
                _lastPrecacheFingerprint = fingerprint;
            _logger.LogInformation("Discovery precache finished: {Prepared} prepared, {Failed} failed.", prepared, failed);
            return prepared;
        }
    }
}
