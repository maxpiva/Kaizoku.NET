using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Series;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Collections.Concurrent;
using ExtensionChapter = Mihon.ExtensionsBridge.Models.Extensions.Chapter;
using ExtensionManga = Mihon.ExtensionsBridge.Models.Extensions.Manga;

namespace KaizokuBackend.Services.Search
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
                var validSeries = linkedSeries.Where(ls => !string.IsNullOrEmpty(ls.MihonId)).ToList();
                var maxConcurrency = Math.Min(appSettings.NumberOfSimultaneousSearches, validSeries.Count);
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
                        var fullData = await source.GetDetailsAsync(m, token).ConfigureAwait(false);
                        var chapterData = await source.GetChaptersAsync(m, token).ConfigureAwait(false);
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
                    }
                    catch (HttpRequestException r)
                    {
                        _logger.LogWarning("Error fetching series details for {Title} from {Provider}: Http Error {StatusCode}.", ls.Title, ls.Provider, r.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching details for series ID {Title}: {Message}", ls.Title, ex.Message);
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
                        var existingProvider = existingForProvider.FirstOrDefault(a => 
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
                return new AugmentedResponseDto();
            }
        }
    }
}
