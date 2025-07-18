using System.Collections.Concurrent;
using System.Net;
using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Downloads;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Settings;
using KaizokuBackend.Services.Series; // <-- Add this for SeriesExtensions
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Series
{
    /// <summary>
    /// Service responsible for series command operations (Create, Update, Delete)
    /// </summary>
    public class SeriesCommandService
    {
        private readonly AppDbContext _db;
        private readonly SettingsService _settings;
        private readonly ArchiveHelperService _archiveHelper;
        private readonly SeriesProviderService _providerService;
        private readonly ContextProvider _baseUrl;
        private readonly ILogger<SeriesCommandService> _logger;
        private readonly SuwayomiClient _suwayomi;
        private readonly DownloadCommandService _downloadCommand;


        public SeriesCommandService(AppDbContext db, SettingsService settings, ArchiveHelperService archiveHelper,
            SeriesProviderService providerService, ContextProvider baseUrl, ILogger<SeriesCommandService> logger,
            SuwayomiClient suwayomi, DownloadCommandService downloadCommand)
        {
            _db = db;
            _settings = settings;
            _archiveHelper = archiveHelper;
            _providerService = providerService;
            _baseUrl = baseUrl;
            _logger = logger;
            _suwayomi = suwayomi;
            _downloadCommand = downloadCommand;
  
        }

        /// <summary>
        /// Adds a new series to the database
        /// </summary>
        /// <param name="fullSeries">Full series information to add</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>The ID of the created series</returns>
        public async Task<Guid> AddSeriesAsync(AugmentedResponse fullSeries, CancellationToken token = default)
        {
            Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (fullSeries == null || fullSeries.Series.Count == 0)
            {
                throw new ArgumentException("No series provided to add");
            }

            using var transaction = await _db.Database.BeginTransactionAsync(token);
            try
            {
                var paths = await _db.GetPathsAsync(token).ConfigureAwait(false);
                string? existingThumb = null;
                List<SeriesProvider> existingProviders = [];
                Models.Database.Series? dbSeries = null;
                
                if (fullSeries.ExistingSeriesId.HasValue)
                {
                    dbSeries = await _db.Series.FirstAsync(s => s.Id == fullSeries.ExistingSeriesId, token)
                        .ConfigureAwait(false);
                    fullSeries.StorageFolderPath = dbSeries.StoragePath;
                }
                else
                {
                    dbSeries = await FindExistingSeriesAsync(fullSeries, settings, paths, token);
                    if (dbSeries != null)
                        existingThumb = dbSeries.ThumbnailUrl;
                }

                if (dbSeries != null)
                {
                    existingProviders = await _db.SeriesProviders.Where(a => a.SeriesId == dbSeries.Id)
                        .ToListAsync(token).ConfigureAwait(false);
                }

                existingProviders = ProcessSeriesProviders(fullSeries, existingProviders);

                dbSeries = await ConsolidateDBSeriesFromProvidersAsync(dbSeries, existingProviders,
                    fullSeries.StorageFolderPath, fullSeries.DisableJobs, token).ConfigureAwait(false);
                
                existingProviders.ForEach(a => a.SeriesId = dbSeries.Id);
                existingProviders.CalculateContinueAfterChapter();
                
                await _providerService.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
                    existingProviders, [], token).ConfigureAwait(false);
                
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                
                await _providerService.RescheduleIfNeededAsync(existingProviders, true, dbSeries.PauseDownloads, token)
                    .ConfigureAwait(false);
                
                await dbSeries.SaveKaizokuInfoToDirectoryAsync(
                    Path.Combine(settings.StorageFolder, dbSeries.StoragePath), _logger, token);
                
                if (existingThumb != dbSeries.ThumbnailUrl)
                {
                    await _archiveHelper.WriteComicThumbnailAsync(dbSeries, token).ConfigureAwait(false);
                }

                return dbSeries.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AddSeries: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Updates an existing series
        /// </summary>
        /// <param name="series">Series information to update</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Updated series extended information</returns>
        public async Task<SeriesExtendedInfo> UpdateSeriesAsync(SeriesExtendedInfo series, CancellationToken token = default)
        {
            if (series == null || series.Id == Guid.Empty)
            {
                throw new ArgumentException("Invalid series data provided for update");
            }

            Models.Database.Series? dbSeries = await _db.Series.Include(s => s.Sources)
                .FirstOrDefaultAsync(s => s.Id == series.Id, token).ConfigureAwait(false);
            if (dbSeries == null)
            {
                throw new KeyNotFoundException($"Series with ID {series.Id} not found");
            }

            Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            string existingThumb = dbSeries.ThumbnailUrl;

            // Update provider settings
            UpdateProviderSettings(series, dbSeries);

            List<int> deletedSources = await _providerService.DeleteSourcesIfNeededAsync(series, dbSeries, token)
                .ConfigureAwait(false);
            
            dbSeries = await ConsolidateDBSeriesFromProvidersAsync(dbSeries, dbSeries.Sources.ToList(),
                dbSeries.StoragePath, dbSeries.PauseDownloads, token);
            
            dbSeries.Sources.CalculateContinueAfterChapter();
            dbSeries.PauseDownloads = series.PauseDownloads;
            
            _db.Series.Update(dbSeries);
            
            await _providerService.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
                dbSeries.Sources, deletedSources, token).ConfigureAwait(false);
            
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            
            await _providerService.RescheduleIfNeededAsync(dbSeries.Sources, true, series.PauseDownloads, token)
                .ConfigureAwait(false);
            
            await dbSeries.SaveKaizokuInfoToDirectoryAsync(
                Path.Combine(settings.StorageFolder, dbSeries.StoragePath), _logger, token);
            
            if (existingThumb != dbSeries.ThumbnailUrl)
            {
                await _archiveHelper.WriteComicThumbnailAsync(dbSeries, token).ConfigureAwait(false);
            }

            return dbSeries.ToSeriesExtendedInfo(_baseUrl, settings);
        }

        /// <summary>
        /// Deletes a series from the database
        /// </summary>
        /// <param name="id">Series ID to delete</param>
        /// <param name="alsoPhysical">Whether to also delete physical files</param>
        /// <param name="token">Cancellation token</param>
        public async Task DeleteSeriesAsync(Guid id, bool alsoPhysical, CancellationToken token = default)
        {
            Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (id == Guid.Empty)
            {
                throw new ArgumentException("Invalid Series Guid provided for delete");
            }

            Models.Database.Series? dbSeries = await _db.Series.Include(s => s.Sources)
                .FirstOrDefaultAsync(s => s.Id == id, token).ConfigureAwait(false);
            if (dbSeries == null)
            {
                throw new KeyNotFoundException($"Series with ID {id} not found");
            }

            List<int> deletedSeries = dbSeries.Sources.Select(a => a.SuwayomiId).ToList();
            
            if (alsoPhysical)
                dbSeries.DeletePhysicalSeries(settings, _logger);
            
            foreach (SeriesProvider p in dbSeries.Sources)
            {
                await _providerService.RescheduleIfNeededAsync([p], false, true, token).ConfigureAwait(false);
            }

            _db.Series.Remove(dbSeries);
            
            await _providerService.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
                [], deletedSeries, token).ConfigureAwait(false);
            
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        

        /// <summary>
        /// Updates a source with latest series information (moved from SeriesUpdateService)
        /// </summary>
        public async Task<JobResult> UpdateSourceAsync(SuwayomiSource source, CancellationToken token)
        {
            try
            {
                Dictionary<int, (DateTime, SuwayomiChapter?)> latestDates = await _db.LatestSeries.Where(a => a.SuwayomiSourceId == source.Id).ToDictionaryAsync(a => a.SuwayomiId, a => (a.FetchDate, a.Chapters.OrderByDescending(b => b.Index).FirstOrDefault()), token).ConfigureAwait(false);
                ConcurrentDictionary<int, ComboSeries> newChaps = [];
                int page = 1;
                bool upToDate = false;
                bool neverDone = latestDates.Count == 0;

                do
                {
                    SuwayomiGraphQLSeriesResult? res = await _suwayomi.GetLatestAsync(source.Id, page, token);
                    if (res == null)
                    {
                        _logger.LogError("Unable to get Latest Series from {Name}:{Lang}", source.Name, source.Lang);
                        return JobResult.Failed;
                    }

                    Models.Settings s = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                    await Parallel.ForEachAsync(res.Mangas, new ParallelOptions
                        {
                            CancellationToken = token,
                            MaxDegreeOfParallelism = s.NumberOfSimultaneousDownloadsPerProvider
                        },
                        async (ss, b) =>
                        {
                            if (upToDate)
                                return;
                            ComboSeries s = new ComboSeries();
                            s.Id = ss.Id;
                            if (!latestDates.TryGetValue(ss.Id, out (DateTime, SuwayomiChapter?) value) ||
                                (value.Item1.AddDays(7) < DateTime.UtcNow))
                            {
                                s.Series = await _suwayomi.GetFullSeriesDataAsync(ss.Id, true, token)
                                    .ConfigureAwait(false);
                                if (s.Series == null)
                                {
                                    _logger.LogWarning("Unable to get Series {Title} from {Name}:{Lang}", ss.Title,
                                        source.Name, source.Lang);
                                    return;
                                }

                                newChaps[s.Id] = s;
                            }

                            List<SuwayomiChapter>? chaps =
                                await _suwayomi.GetChaptersAsync(ss.Id, true, token).ConfigureAwait(false);
                            if (chaps == null)
                            {
                                _logger.LogWarning("Unable to get Series {Title} Chapters from {Name}:{Lang}", ss.Title,
                                    source.Name, source.Lang);
                                newChaps.Remove(ss.Id, out _);
                                return;
                            }

                            s.Chapters = chaps;
                            SuwayomiChapter? latest_online = chaps.OrderByDescending(a => a.Index).FirstOrDefault();
                            if (latest_online != null && latestDates.TryGetValue(s.Id, out (DateTime, SuwayomiChapter?) value2) && value2.Item2 != null)
                            {
                                if ((latestDates[s.Id].Item2!.Index >= latest_online.Index) &&
                                    (latestDates[s.Id].Item2!.UploadDate >= latest_online.UploadDate))
                                {
                                    upToDate = true;
                                }
                            }
                        }).ConfigureAwait(false);
                    if (upToDate)
                        break;
                    page++;
                } while (!upToDate && !neverDone);

                List<int> ids = newChaps.Keys.ToList();
                List<LatestSerie> toUpdate = await _db.LatestSeries.Where(a => ids.Contains(a.SuwayomiId)).ToListAsync(token).ConfigureAwait(false);
                List<(LatestSerie, SeriesProvider)> toCheck = [];
                
                foreach (ComboSeries c in newChaps.Values)
                {
                    LatestSerie? s = toUpdate.FirstOrDefault(a => a.SuwayomiId == c.Id);
                    if (s == null)
                    {
                        s = new LatestSerie();
                        _db.LatestSeries.Add(s);
                    }
                    if (c.Series != null)
                        s.PopulateSeries(source, c.Series);
                    s.Chapters = c.Chapters;
                    SuwayomiChapter? latest_online = s.Chapters.OrderByDescending(a => a.Index).FirstOrDefault();
                    DateTime latestUTC = DateTimeOffset.FromUnixTimeMilliseconds(latest_online?.UploadDate ?? 0).DateTime;

                    if (latestUTC > DateTime.UtcNow || latestUTC.AddMonths(1) < DateTime.UtcNow)
                    {
                        latestUTC = DateTime.UtcNow;
                    }
                    s.FetchDate = latestUTC;
                    s.LatestChapter = latest_online?.ChapterNumber;
                    s.ChapterCount = s.Chapters.Count;
                    s.LatestChapterTitle = latest_online?.Name ?? "";
                    SeriesProvider? serie = await _db.SeriesProviders
                        .Where(a => a.SuwayomiId == s.SuwayomiId).AsNoTracking()
                        .FirstOrDefaultAsync(token).ConfigureAwait(false);
                    s.InLibrary = InLibraryStatus.NotInLibrary;
                    if (serie != null)
                    {
                        s.SeriesId = serie.SeriesId;
                        if (serie.IsDisabled || serie.IsUninstalled)
                            s.InLibrary = InLibraryStatus.InLibraryButDisabled;
                        else
                        {
                            toCheck.Add((s, serie));
                            s.InLibrary = InLibraryStatus.InLibrary;
                        }
                    }
                }
                await _db.SaveChangesAsync(token).ConfigureAwait(false);

                foreach (var u in toCheck)
                {
                    Models.Database.Series series = await _db.Series.Include(a => a.Sources)
                        .Where(a => a.Id == u.Item2.SeriesId).AsNoTracking().FirstAsync(token).ConfigureAwait(false);
                    if (!series.PauseDownloads)
                    {
                        List<ChapterDownload> chaps = series.GenerateDownloadsFromChapterData(u.Item2, u.Item1.Chapters);
                        if (chaps.Count > 0)
                        {
                            await _downloadCommand.QueueChapterDownloadsAsync(u.Item2, chaps, token).ConfigureAwait(false);
                        }
                    }
                }

                return JobResult.Success;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error Updating Source : {Message}", e.Message);
                return JobResult.Failed;
            }
        }

        /// <summary>
        /// Downloads/updates a specific series provider (moved from SeriesUpdateService)
        /// </summary>
        public async Task<JobResult> DownloadSeriesAsync(Guid seriesProvider, CancellationToken token = default)
        {
            SeriesProvider? serie = await _db.SeriesProviders.Where(s => s.Id == seriesProvider).AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
            if (serie == null)
            {
                _logger.LogWarning("Series Provider {SeriesProvider} no longer exists", seriesProvider);
                return JobResult.Delete;
            }
            if (serie.IsDisabled || serie.IsUninstalled)
            {
                _logger.LogWarning("Series Provider {SeriesProvider} is disabled or uninstalled", seriesProvider);
                return JobResult.Failed;
            }
            var series = await _db.Series.Include(a => a.Sources).Where(s => s.Id == serie.SeriesId).AsNoTracking().FirstAsync(token).ConfigureAwait(false);
            var chapterData = await _suwayomi.GetChaptersAsync(serie.SuwayomiId, true, token).ConfigureAwait(false);
            List<ChapterDownload> chaps = series.GenerateDownloadsFromChapterData(serie, chapterData);
            return await _downloadCommand.QueueChapterDownloadsAsync(serie, chaps, token).ConfigureAwait(false);
        }

        // Private helper methods
        private async Task<Models.Database.Series?> FindExistingSeriesAsync(AugmentedResponse fullSeries, 
            Models.Settings settings, Dictionary<string, Guid> paths, CancellationToken token)
        {
            if (fullSeries.StorageFolderPath.StartsWith(settings.StorageFolder))
                fullSeries.StorageFolderPath = fullSeries.StorageFolderPath[settings.StorageFolder.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            fullSeries.StorageFolderPath = settings.StorageFolder.GetActualDirectoryPathCaseInsensitive(
                fullSeries.StorageFolderPath);

            if (paths.TryGetValue(fullSeries.StorageFolderPath, out Guid id))
            {
                return await _db.Series.FirstOrDefaultAsync(s => s.Id == id, token).ConfigureAwait(false);
            }

            // Search by title similarity
            var allProvs = await _db.SeriesProviders.Select(a => new { a.Title, a.SeriesId })
                .ToListAsync(token).ConfigureAwait(false);
            
            foreach (var n in allProvs)
            {
                foreach (var ser in fullSeries.Series)
                {
                    if (n.Title.AreStringSimilar(ser.Title, 0))
                    {
                        return await _db.Series.FirstOrDefaultAsync(a => a.Id == n.SeriesId, token)
                            .ConfigureAwait(false);
                    }
                }
            }

            return null;
        }

        private List<SeriesProvider> ProcessSeriesProviders(AugmentedResponse fullSeries, List<SeriesProvider> existingProviders)
        {
            List<ProviderInfo> pInfos = fullSeries.LocalInfo?.Providers ?? [];

            foreach (var fs in fullSeries.Series)
            {
                ProviderInfo? pInfo = FindMatchingProviderInfo(pInfos, fs);
                if (pInfo != null)
                    pInfos.Remove(pInfo);

                var existingProvider = existingProviders.FirstOrDefault(sp => sp.IsMatchingProvider(fs));
                if (existingProvider != null)
                {
                    string provider = fs.Provider;
                    if (!string.IsNullOrEmpty(fs.Scanlator))
                        provider += "-" + fs.Scanlator;
                    
                    _logger.LogInformation("Found existing Provider for '{Title}': {Lang}/{provider}.",
                        fs.Title, fs.Lang, provider);
                    
                    InternalCreateOrUpdateProviderFromFullSeries(fs, existingProvider);
                }
                else
                {
                    existingProvider = InternalCreateOrUpdateProviderFromFullSeries(fs);
                    _db.SeriesProviders.Add(existingProvider);
                    existingProviders.Add(existingProvider);
                }

                if (pInfo != null)
                {
                    InternalAssignArchives(existingProvider, pInfo.Archives);
                    _db.Touch(existingProvider, a => a.Chapters);
                }
            }

            // Add remaining provider infos
            foreach (ProviderInfo p in pInfos)
            {
                var nProvider = p.ToSeriesProvider();
                InternalAssignArchives(nProvider, p.Archives);
                _db.SeriesProviders.Add(nProvider);
                existingProviders.Add(nProvider);
            }

            return existingProviders;
        }

        private static ProviderInfo? FindMatchingProviderInfo(List<ProviderInfo> pInfos, FullSeries fs)
        {
            foreach (ProviderInfo p in pInfos)
            {
                if (string.IsNullOrEmpty(p.Scanlator))
                {
                    if (fs.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase) &&
                        fs.Lang.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return p;
                    }
                }
                else
                {
                    if (fs.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase) &&
                        (fs.Scanlator.Equals(p.Scanlator, StringComparison.InvariantCultureIgnoreCase) ||
                         fs.Scanlator.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase)) &&
                        fs.Lang.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return p;
                    }
                }
            }
            return null;
        }

        private static void UpdateProviderSettings(SeriesExtendedInfo series, Models.Database.Series dbSeries)
        {
            foreach (ProviderExtendedInfo p in series.Providers)
            {
                SeriesProvider? n = dbSeries.Sources.FirstOrDefault(a => a.Id == p.Id);
                if (n == null)
                    continue;
                
                n.IsDisabled = p.IsDisabled;
                n.IsStorage = p.IsStorage;
                n.IsTitle = p.UseTitle;
                n.IsCover = p.UseCover;
                n.ContinueAfterChapter = p.ContinueAfterChapter;
            }
        }

        private void InternalAssignArchives(SeriesProvider provider, List<ArchiveInfo>? archives)
        {
            provider.AssignArchives(archives);
            _db.Touch(provider, e => e.Chapters);
        }

        private SeriesProvider InternalCreateOrUpdateProviderFromFullSeries(FullSeries fs, SeriesProvider? provider = null)
        {
            provider = fs.CreateOrUpdate(provider);
            _db.Touch(provider, e => e.Chapters);
            return provider;
        }

        private async Task<Models.Database.Series> ConsolidateDBSeriesFromProvidersAsync(Models.Database.Series? dbSeries,
            List<SeriesProvider> providers, string path, bool startDisabled, CancellationToken token = default)
        {
            var consolidatedSeries = providers.ToFullSeries();
            
            if (dbSeries != null)
            {
                dbSeries.FillSeriesFromFullSeries(consolidatedSeries);
            }
            else
            {
                dbSeries = consolidatedSeries.ToSeries(path);
                dbSeries.PauseDownloads = startDisabled;
                await _db.Series.AddAsync(dbSeries, token).ConfigureAwait(false);
            }

            return dbSeries;
        }

        /// <summary>
        /// Internal class for managing series and chapter combinations during updates (moved from SeriesUpdateService)
        /// </summary>
        private class ComboSeries
        {
            public int Id { get; set; }
            public SuwayomiSeries? Series { get; set; }
            public List<SuwayomiChapter> Chapters { get; set; } = [];
        }
    }
}