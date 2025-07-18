using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Extensions;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Settings;
using KaizokuBackend.Services.Series;

namespace KaizokuBackend.Services.Search
{
    /// <summary>
    /// Service for search command operations following CQRS pattern
    /// </summary>
    public class SearchCommandService
    {
        private readonly SuwayomiClient _suwayomi;
        private readonly SettingsService _settings;
        private readonly ContextProvider _baseUrl;
        private readonly AppDbContext _db;
        private readonly ILogger<SearchCommandService> _logger;

        public SearchCommandService(
            SuwayomiClient suwayomi,
            SettingsService settings,
            ContextProvider baseUrl,
            AppDbContext db,
            ILogger<SearchCommandService> logger)
        {
            _suwayomi = suwayomi;
            _settings = settings;
            _baseUrl = baseUrl;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Augments a list of LinkedSeries with full details by fetching complete information from Suwayomi
        /// </summary>
        /// <param name="linkedSeries">List of linked series to augment</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Augmented response with complete series information</returns>
        public async Task<AugmentedResponse> AugmentSeriesAsync(List<LinkedSeries> linkedSeries, CancellationToken token = default)
        {
            if (linkedSeries == null || linkedSeries.Count == 0)
            {
                return new AugmentedResponse();
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
                var seriesDetailsMap = new ConcurrentDictionary<string, SuwayomiSeries>();
                var validSeries = linkedSeries.Where(ls => !string.IsNullOrEmpty(ls.Id) && int.TryParse(ls.Id, out _)).ToList();
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
                        if (int.TryParse(ls.Id, out int seriesId))
                        {
                            var fullData = await _suwayomi.GetFullSeriesDataAsync(seriesId, true, ct).ConfigureAwait(false);
                            var chapterData = await _suwayomi.GetChaptersAsync(seriesId, true, ct).ConfigureAwait(false);
                            
                            if (fullData != null && chapterData != null && chapterData.Count > 0)
                            {
                                // Set default scanlator if not provided
                                chapterData.ForEach(a =>
                                {
                                    if (string.IsNullOrEmpty(a.Scanlator))
                                        a.Scanlator = ls.Provider;
                                });
                                
                                fullData.Chapters = chapterData;
                                seriesDetailsMap.TryAdd(ls.Id, fullData);
                            }
                        }
                    }
                    catch (HttpRequestException r)
                    {
                        _logger.LogWarning("Error fetching series details for {Title} from {Provider}: Http Error {StatusCode}.", ls.Title, ls.Provider, r.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching details for series ID {Id}: {Message}", ls.Id, ex.Message);
                    }
                }).ConfigureAwait(false);

                // Convert to FullSeries objects
                var fullSeriesResults = new List<FullSeries>();
                var categories = appSettings.Categories ?? [];

                foreach (var ls in linkedSeries)
                {
                    if (string.IsNullOrEmpty(ls.Id) || !seriesDetailsMap.TryGetValue(ls.Id, out var details))
                    {
                        continue;
                    }

                    details.Chapters.FillMissingChapterNumbers();

                    var fullSeries = new FullSeries
                    {
                        Id = ls.Id,
                        ProviderId = ls.ProviderId,
                        Provider = ls.Provider,
                        Scanlator = ls.Provider,
                        Lang = ls.Lang,
                        Title = details.Title,
                        ThumbnailUrl = _baseUrl.RewriteSeriesThumbnail(details),
                        Artist = details.Artist ?? string.Empty,
                        Author = details.Author ?? string.Empty,
                        Description = details.Description ?? string.Empty,
                        Genre = details.Genre ?? new List<string>(),
                        ChapterCount = details.ChapterCount.HasValue ? (int)details.ChapterCount.Value : 0,
                        Url = details.RealUrl,
                        Meta = details.Meta ?? new Dictionary<string, string>(),
                        SuggestedFilename = details.Title.MakeFolderNameSafe(),
                        Status = details.Status,
                        IsStorage = ls.IsStorage,
                    };

                    fullSeries.Type = fullSeries.Genre.DeriveTypeFromGenre(categories);

                    // Group chapters by scanlator
                    var groupedChapters = details.Chapters
                        .GroupBy(c => c.Scanlator)
                        .ToDictionary(g => g.Key ?? "", g => g.ToList());

                    var seriesPerScanlator = new List<FullSeries>();
                    foreach (var scanlatorGroup in groupedChapters)
                    {
                        var seriesCopy = FastDeepCloner.DeepCloner.Clone(fullSeries);
                        var firstChapter = scanlatorGroup.Value.First();
                        
                        seriesCopy.Scanlator = scanlatorGroup.Key;
                        seriesCopy.LastUpdatedUTC = DateTimeOffset.FromUnixTimeSeconds(firstChapter.UploadDate / 1000).UtcDateTime;
                        seriesCopy.ChapterCount = scanlatorGroup.Value.Count;
                        seriesCopy.Chapters = scanlatorGroup.Value.Select(a => a.ToChapter()).OrderBy(a => a.ProviderIndex).ToList();
                        seriesCopy.ChapterList = scanlatorGroup.Value.Where(a => a.ChapterNumber != null).Select(a => a.ChapterNumber!.Value).FormatDecimalRanges();
                        
                        seriesPerScanlator.Add(seriesCopy);
                    }

                    // Apply existing provider logic
                    var existingForProvider = existingSeries.Where(a => a.Provider == ls.Provider && a.Language == ls.Lang && ls.Title == a.Title).ToList();
                    foreach (var fullSeriesItem in seriesPerScanlator)
                    {
                        var existingProvider = existingForProvider.FirstOrDefault(a => 
                            a.Title == fullSeriesItem.Title && 
                            a.Language == fullSeriesItem.Lang && 
                            a.Scanlator == fullSeriesItem.Scanlator);
                        
                        if (existingProvider != null)
                        {
                            fullSeriesItem.ExistingProvider = true;
                            if (existingProvider.Status == SeriesStatus.ONGOING && existingProvider.Chapters.Count > 0)
                                fullSeriesItem.ContinueAfterChapter = (int)(existingProvider.Chapters.Max(a => a.Number) ?? 0m);
                            else
                                fullSeriesItem.ContinueAfterChapter = null;
                        }
                    }

                    fullSeriesResults.AddRange(seriesPerScanlator);
                }

                // Apply type derivation logic
                if (fullSeriesResults.All(a => a.Type == null))
                {
                    fullSeriesResults.ForEach(a => { a.Type = a.Genre.DeriveTypeFromGenre(categories, true); });
                }

                var inferredType = fullSeriesResults.FirstOrDefault(a => a.Type != null)?.Type;
                if (inferredType != null)
                {
                    fullSeriesResults.Where(a => a.Type == null).ToList().ForEach(a => a.Type = inferredType);
                }

                return new AugmentedResponse
                {
                    Series = fullSeriesResults,
                    StorageFolderPath = appSettings.StorageFolder,
                    UseCategoriesForPath = appSettings.CategorizedFolders,
                    Categories = appSettings.Categories?.ToList() ?? [],
                    PreferredLanguages = appSettings.PreferredLanguages.ToList(),
                    ExistingSeries = fullSeriesResults.Any(a => a.ExistingProvider)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AugmentSeriesAsync: {Message}", ex.Message);
                return new AugmentedResponse();
            }
        }
    }
}