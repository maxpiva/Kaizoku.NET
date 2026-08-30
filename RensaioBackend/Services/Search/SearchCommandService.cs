using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Models;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Helpers;
using RensaioBackend.Services.Import;
using RensaioBackend.Services.Series;
using RensaioBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Collections.Concurrent;
using ExtensionChapter = Mihon.ExtensionsBridge.Models.Extensions.Chapter;
using ExtensionManga = Mihon.ExtensionsBridge.Models.Extensions.Manga;

namespace RensaioBackend.Services.Search
{
    /// <summary>
    /// Service for search command operations following CQRS pattern
    /// </summary>
    public class SearchCommandService
    {

        private readonly SettingsService _settings;

        private readonly AppDbContext _db;
        private readonly ILogger<SearchCommandService> _logger;
        private readonly MihonBridgeService _mihon;

        public SearchCommandService(
            SettingsService settings,
            AppDbContext db,
            MihonBridgeService mihon,
            ILogger<SearchCommandService> logger)
        {            
            _settings = settings;
            _db = db;
            _logger = logger;
            _mihon = mihon;
        }

        /// <summary>
        /// Augments a list of LinkedSeries with full details by fetching complete information from Suwayomi
        /// </summary>
        /// <param name="linkedSeries">List of linked series to augment</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Augmented response with complete series information</returns>
        public async Task<AugmentedResponseDto> AugmentSeriesAsync(List<LinkedSeriesDto> linkedSeries, CancellationToken token = default)
        {
            if (linkedSeries == null || linkedSeries.Count == 0)
            {
                return new AugmentedResponseDto();
            }
            try
            {
                var appSettings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                var providerTitles = linkedSeries.Select(a => a.Title).ToList();

                // Get existing series providers to check for continuation logic
                var existingSeries = await _db.SeriesProviders
                    .Where(sp => providerTitles.Contains(sp.Title))
                    .AsNoTracking()
                    .ToListAsync(token).ConfigureAwait(false);
                
                existingSeries = existingSeries.Where(a => linkedSeries.Any(ls => ls.Lang == a.Language && ls.Title == a.Title)).ToList();

                // Fetch full series data in parallel
                var seriesDetailsMap = new ConcurrentDictionary<string, (ParsedManga, List<ParsedChapter>)>();
                var sourceErrors = new ConcurrentBag<AugmentSourceErrorDto>();
                var validSeries = linkedSeries.Where(ls => !string.IsNullOrEmpty(ls.MihonId)).ToList();
                foreach (var ls in linkedSeries.Where(ls => string.IsNullOrEmpty(ls.MihonId)))
                {
                    sourceErrors.Add(new AugmentSourceErrorDto { Provider = ls.Provider, Title = ls.Title, Reason = "Source is not installed or unavailable." });
                }
                // Cap concurrency: each details/chapters fetch crosses the IKVM boundary and consumes
                // process thread budget shared with CEF (see SourceTimeoutGate).
                var maxConcurrency = Math.Min(appSettings.NumberOfSimultaneousSearches, Math.Min(6, validSeries.Count));
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = token
                };
                  
                await Parallel.ForEachAsync(validSeries, parallelOptions, async (ls, ct) =>
                {
                    try
                    {
                        var source = await _mihon.SourceFromProviderIdAsync(ls.MihonProviderId!, token).ConfigureAwait(false);
                        Manga m = ls.ToManga()!;
                        // Bound each source call so a stuck provider can't freeze the import.
                        var mangaUpdate = await SourceTimeout
                            .RunAsync(c => source.GetDetailsAndChaptersAsync(m, c), ct)
                            .ConfigureAwait(false);
                        var fullData = mangaUpdate.Manga;
                        var chapterData = mangaUpdate.Chapters;
                        if (fullData != null && chapterData != null && chapterData.Count > 0)
                        {
                            // Set default scanlator if not provided
                            chapterData.ForEach(a =>
                            {
                                if (string.IsNullOrEmpty(a.Scanlator))
                                    a.Scanlator = ls.Provider;
                            });
                            seriesDetailsMap.TryAdd(ls.MihonId!, (fullData, chapterData));
                        }
                        else
                        {
                            var reason = fullData == null ? "Source returned no details for this series." : "Source has no readable chapters for this series.";
                            _logger.LogWarning("Skipping {Title} from {Provider}: {Reason}", ls.Title, ls.Provider, reason);
                            sourceErrors.Add(new AugmentSourceErrorDto { Provider = ls.Provider, Title = ls.Title, Reason = reason });
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw; // the job itself was cancelled
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("Fetching details for {Title} from {Provider} timed out after {Seconds}s; skipping.", ls.Title, ls.Provider, SourceTimeout.DefaultTimeout.TotalSeconds);
                        sourceErrors.Add(new AugmentSourceErrorDto { Provider = ls.Provider, Title = ls.Title, Reason = $"Timed out after {SourceTimeout.DefaultTimeout.TotalSeconds:0}s." });
                    }
                    catch (HttpRequestException r)
                    {
                        _logger.LogWarning("Error fetching series details for {Title} from {Provider}: Http Error {StatusCode}. {Message}", ls.Title, ls.Provider, r.StatusCode, r.Message);
                        sourceErrors.Add(new AugmentSourceErrorDto { Provider = ls.Provider, Title = ls.Title, Reason = r.StatusCode != null ? $"HTTP error {(int)r.StatusCode} ({r.StatusCode})." : $"Connection error: {r.Message}" });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching details for series ID {Title}: {Message}", ls.Title, ex.Message);
                        sourceErrors.Add(new AugmentSourceErrorDto { Provider = ls.Provider, Title = ls.Title, Reason = ex.Message });
                    }
                }).ConfigureAwait(false);

                // Convert to ProviderSeriesDetails objects
                var ProviderSeriesDetailsResults = new List<ProviderSeriesDetails>();
                var categories = appSettings.Categories ?? [];

                foreach (var ls in linkedSeries)
                {
                    if (string.IsNullOrEmpty(ls.MihonId) || !seriesDetailsMap.TryGetValue(ls.MihonId, out var details))
                    {
                        continue;
                    }

                    details.Item2.FillMissingChapterNumbers();

                    var ProviderSeriesDetails = new ProviderSeriesDetails
                    {
                        MihonId = ls.MihonId,
                        MihonProviderId = ls.MihonProviderId,
                        BridgeItemInfo = ls.BridgeItemInfo,
                        Provider = ls.Provider,
                        Scanlator = ls.Provider,
                        Lang = ls.Lang,
                        Title = details.Item1.Title,
                        ThumbnailUrl = details.Item1.ThumbnailUrl,
                        Artist = details.Item1.Artist ?? string.Empty,
                        Author = details.Item1.Author ?? string.Empty,
                        Description = details.Item1.Description ?? string.Empty,
                        Genre = details.Item1.GetGenres(),
                        ChapterCount = details.Item2?.Count ?? 0,
                        Url = details.Item1.RealUrl,
                        SuggestedFilename = details.Item1.Title.MakeFolderNameSafe(),
                        Status = (SeriesStatus)(int)details.Item1.Status,
                        IsStorage = ls.IsStorage,
                    };

                    ProviderSeriesDetails.Type = ProviderSeriesDetails.Genre.DeriveTypeFromGenre(categories);

                    // Group chapters by scanlator
                    var groupedChapters = details.Item2?
                        .GroupBy(c => c.Scanlator)
                        .ToDictionary(g => g.Key ?? "", g => g.ToList());

                    var seriesPerScanlator = new List<ProviderSeriesDetails>();
                    foreach (var scanlatorGroup in groupedChapters)
                    {
                        var seriesCopy = FastDeepCloner.DeepCloner.Clone(ProviderSeriesDetails);
                        var firstChapter = scanlatorGroup.Value.First();
                        
                        seriesCopy.Scanlator = scanlatorGroup.Key;
                        seriesCopy.LastUpdatedUTC = firstChapter.DateUpload.DateTime;
                        seriesCopy.ChapterCount = scanlatorGroup.Value.Count;
                        seriesCopy.Chapters = scanlatorGroup.Value.Select(a => a.ToChapter()).OrderBy(a => a.ProviderIndex).ToList();
                        seriesCopy.ChapterList = scanlatorGroup.Value.Select(a => a.ParsedNumber).FormatDecimalRanges();
                        
                        seriesPerScanlator.Add(seriesCopy);
                    }

                    // Apply existing provider logic
                    var existingForProvider = existingSeries.Where(a => a.MihonProviderId == ls.MihonProviderId && a.Language == ls.Lang && ls.Title == a.Title).ToList();
                    foreach (var ProviderSeriesDetailsItem in seriesPerScanlator)
                    {
                        var existingProvider = existingForProvider.FirstOrDefault(a => a.MihonProviderId == ProviderSeriesDetailsItem.MihonProviderId && 
                            a.Title == ProviderSeriesDetailsItem.Title && 
                            a.Language == ProviderSeriesDetailsItem.Lang && 
                            a.Scanlator == ProviderSeriesDetailsItem.Scanlator);
                        
                        if (existingProvider != null)
                        {
                            ProviderSeriesDetailsItem.ExistingProvider = true;
                            if (existingProvider.Status == SeriesStatus.ONGOING && existingProvider.Chapters.Count > 0)
                                ProviderSeriesDetailsItem.ContinueAfterChapter = (int)(existingProvider.Chapters.Max(a => a.Number) ?? 0m);
                            else
                                ProviderSeriesDetailsItem.ContinueAfterChapter = null;
                        }
                    }

                    ProviderSeriesDetailsResults.AddRange(seriesPerScanlator);
                }

                // Apply type derivation logic
                if (ProviderSeriesDetailsResults.All(a => a.Type == null))
                {
                    ProviderSeriesDetailsResults.ForEach(a => { a.Type = a.Genre.DeriveTypeFromGenre(categories, true); });
                }

                var inferredType = ProviderSeriesDetailsResults.FirstOrDefault(a => a.Type != null)?.Type;
                if (inferredType != null)
                {
                    ProviderSeriesDetailsResults.Where(a => a.Type == null).ToList().ForEach(a => a.Type = inferredType);
                }

                return new AugmentedResponseDto
                {
                    Series = ProviderSeriesDetailsResults,
                    SourceErrors = sourceErrors.ToList(),
                    StorageFolderPath = appSettings.StorageFolder,
                    UseCategoriesForPath = appSettings.CategorizedFolders,
                    Categories = appSettings.Categories?.ToList() ?? [],
                    PreferredLanguages = appSettings.PreferredLanguages.ToList(),
                    ExistingSeries = ProviderSeriesDetailsResults.Any(a => a.ExistingProvider)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AugmentSeriesAsync: {Message}", ex.Message);
                return new AugmentedResponseDto
                {
                    SourceErrors = [new AugmentSourceErrorDto { Reason = $"Unexpected error while fetching series details: {ex.Message}" }]
                };
            }
        }
    }
}
