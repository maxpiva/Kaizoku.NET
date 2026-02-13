using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Downloads;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Settings;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using ExtensionChapter = Mihon.ExtensionsBridge.Models.Extensions.Chapter;
using ExtensionManga = Mihon.ExtensionsBridge.Models.Extensions.Manga;

namespace KaizokuBackend.Services.Series
{
    /// <summary>
    /// Service responsible for series command operations (Create, Update, Delete)
    /// </summary>
    public class SeriesCommandService
    {
        private readonly AppDbContext _db;
        private readonly SettingsService _settings;
        private readonly ArchiveHelperService _archiveHelper;        private readonly SeriesProviderService _providerService;

        private readonly ILogger<SeriesCommandService> _logger;
        private readonly DownloadCommandService _downloadCommand;
        private readonly MihonBridgeService _mihon;
        private readonly ThumbCacheService _cache;

        public SeriesCommandService(AppDbContext db, SettingsService settings, ArchiveHelperService archiveHelper,
            SeriesProviderService providerService, ILogger<SeriesCommandService> logger,
            DownloadCommandService downloadCommand, MihonBridgeService mihon, ThumbCacheService cache)
        {
            _db = db;
            _settings = settings;
            _archiveHelper = archiveHelper;
            _providerService = providerService;
            _logger = logger;
            _downloadCommand = downloadCommand;
            _mihon = mihon;
            _cache = cache;
        }

        /// <summary>
        /// Adds a new series to the database
        /// </summary>
        /// <param name="ProviderSeriesDetails">Full series information to add</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>The ID of the created series</returns>
        public async Task<Guid> AddSeriesAsync(AugmentedResponseDto ProviderSeriesDetails, CancellationToken token = default)
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (ProviderSeriesDetails == null || ProviderSeriesDetails.Series.Count == 0)
            {
                throw new ArgumentException("No series provided to add");
            }

            using var transaction = await _db.Database.BeginTransactionAsync(token);
            try
            {
                var paths = await _db.GetPathsAsync(token).ConfigureAwait(false);
                string? existingThumb = null;
                List<SeriesProviderEntity> existingProviders = [];
                Models.Database.SeriesEntity? dbSeries = null;
                
                if (ProviderSeriesDetails.ExistingSeriesId.HasValue)
                {
                    dbSeries = await _db.Series.FirstAsync(s => s.Id == ProviderSeriesDetails.ExistingSeriesId, token)
                        .ConfigureAwait(false);
                    ProviderSeriesDetails.StorageFolderPath = dbSeries.StoragePath;
                }
                else
                {
                    dbSeries = await FindExistingSeriesAsync(ProviderSeriesDetails, settings, paths, token);
                    if (dbSeries != null)
                        existingThumb = dbSeries.ThumbnailUrl;
                }

                if (dbSeries != null)
                {
                    existingProviders = await _db.SeriesProviders.Where(a => a.SeriesId == dbSeries.Id)
                        .ToListAsync(token).ConfigureAwait(false);
                }

                existingProviders = ProcessSeriesProviders(ProviderSeriesDetails, existingProviders);

                dbSeries = await ConsolidateDBSeriesFromProvidersAsync(dbSeries, existingProviders,
                    ProviderSeriesDetails.StorageFolderPath, ProviderSeriesDetails.DisableJobs, ProviderSeriesDetails.StartChapter, token).ConfigureAwait(false);
                
                existingProviders.ForEach(a => a.SeriesId = dbSeries.Id);
                existingProviders.CalculateContinueAfterChapter(ProviderSeriesDetails.StartChapter);
                await _providerService.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
                    existingProviders, [], token).ConfigureAwait(false);
                
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                
                await _providerService.RescheduleIfNeededAsync(existingProviders, true, dbSeries.PauseDownloads, token)
                    .ConfigureAwait(false);
                
                await dbSeries.SaveImportSeriesSnapshotToDirectoryAsync(
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
        public async Task<SeriesExtendedDto> UpdateSeriesAsync(SeriesExtendedDto series, CancellationToken token = default)
        {
            if (series == null || series.Id == Guid.Empty)
            {
                throw new ArgumentException("Invalid series data provided for update");
            }

            Models.Database.SeriesEntity? dbSeries = await _db.Series.Include(s => s.Sources)
                .FirstOrDefaultAsync(s => s.Id == series.Id, token).ConfigureAwait(false);
            if (dbSeries == null)
            {
                throw new KeyNotFoundException($"Series with ID {series.Id} not found");
            }

            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            string existingThumb = dbSeries.ThumbnailUrl;

            // Update provider settings
            UpdateProviderSettings(series, dbSeries);

            List<string> deletedSources = await _providerService.DeleteSourcesIfNeededAsync(series, dbSeries, token)
                .ConfigureAwait(false);
            
            dbSeries = await ConsolidateDBSeriesFromProvidersAsync(dbSeries, dbSeries.Sources.ToList(),
                dbSeries.StoragePath, dbSeries.PauseDownloads, series.StartFromChapter, token);
            
            dbSeries.Sources.CalculateContinueAfterChapter(series.StartFromChapter);
            dbSeries.PauseDownloads = series.PausedDownloads;
            
            _db.Series.Update(dbSeries);
            
            await _providerService.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
                dbSeries.Sources, deletedSources, token).ConfigureAwait(false);
            
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            
            await _providerService.RescheduleIfNeededAsync(dbSeries.Sources, true, series.PausedDownloads, token)
                .ConfigureAwait(false);
            
            await dbSeries.SaveImportSeriesSnapshotToDirectoryAsync(
                Path.Combine(settings.StorageFolder, dbSeries.StoragePath), _logger, token);
            
            if (existingThumb != dbSeries.ThumbnailUrl)
            {
                await _archiveHelper.WriteComicThumbnailAsync(dbSeries, token).ConfigureAwait(false);
            }

            return dbSeries.ToSeriesExtendedInfo(settings);
        }

        /// <summary>
        /// Deletes a series from the database
        /// </summary>
        /// <param name="id">Series ID to delete</param>
        /// <param name="alsoPhysical">Whether to also delete physical files</param>
        /// <param name="token">Cancellation token</param>
        public async Task DeleteSeriesAsync(Guid id, bool alsoPhysical, CancellationToken token = default)
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (id == Guid.Empty)
            {
                throw new ArgumentException("Invalid Series Guid provided for delete");
            }

            Models.Database.SeriesEntity? dbSeries = await _db.Series.Include(s => s.Sources)
                .FirstOrDefaultAsync(s => s.Id == id, token).ConfigureAwait(false);
            if (dbSeries == null)
            {
                throw new KeyNotFoundException($"Series with ID {id} not found");
            }

            List<string> deletedSeries = dbSeries.Sources
                .Select(a => a.MihonId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            
            if (alsoPhysical)
                dbSeries.DeletePhysicalSeries(settings, _logger);
            
            foreach (SeriesProviderEntity p in dbSeries.Sources)
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
        public async Task<JobResult> UpdateSourceAsync(string mihonProviderId, CancellationToken token)
        {
            try
            {
                Dictionary<string, (DateTime, Manga?, ParsedChapter?)> latestDates = await _db.LatestSeries.Where(a => a.MihonProviderId == mihonProviderId).ToDictionaryAsync(a => a.MihonId, a => (a.FetchDate, a.ToManga(), a.Chapters.OrderByDescending(b => b.Index).FirstOrDefault()), token).ConfigureAwait(false);
                ConcurrentDictionary<string, ComboSeries> newChaps = [];
                int page = 1;
                bool upToDate = false;
                bool neverDone = latestDates.Count == 0;
                ISourceInterop src;
                try
                {
                    src = await _mihon.SourceFromProviderIdAsync(mihonProviderId, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to get Latest Series from {mihonProviderId}", mihonProviderId);
                    return JobResult.Failed;
                }
                do
                {
                    MangaList? res;
                    string provider = src.Name + " (" + src.Language + ")";
                    res = await _mihon.MihonErrorWrapperAsync(
                        () => src.GetLatestAsync(page, token),
                        "Unable to get Latest Series from {provider}", provider).ConfigureAwait(false);
                    if (res==null)
                        return JobResult.Failed;

                    SettingsDto s = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
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
                            string mihonId = mihonProviderId + "|" + ss.Url;
                            s.MihonId= mihonId;
                            if (!latestDates.TryGetValue(mihonId, out (DateTime, Manga?, ParsedChapter?) value) ||
                                (value.Item1.AddDays(7) < DateTime.UtcNow))
                            {
                                s.Series = await _mihon.MihonErrorWrapperAsync(
                                    () => src.GetDetailsAsync(ss, token),
                                    "Unable to get Series {Title} from {provider}", ss.Title, provider).ConfigureAwait(false);
                                if (s.Series == null)
                                    return;
                                newChaps[mihonId] = s;
                            }

                            List<ParsedChapter>? chaps = await _mihon.MihonErrorWrapperAsync(
                                () => src.GetChaptersAsync(ss, token),
                                "Unable to get Series {Title} Chapters from {provider}", ss.Title, provider).ConfigureAwait(false);
                            if (chaps == null)
                            {
                                newChaps.Remove(mihonId, out _);
                                return;
                            }

                            s.Chapters = chaps;
                            ParsedChapter? latest_online = chaps.OrderByDescending(a => a.Index).FirstOrDefault();
                            if (latest_online != null && latestDates.TryGetValue(mihonId, out (DateTime, Manga?, ParsedChapter?) value2) && value2.Item2 != null && value2.Item3!=null)
                            {
                                if ((latestDates[mihonId].Item3!.Index >= latest_online.Index) &&
                                    (latestDates[mihonId].Item3!.DateUpload >= latest_online.DateUpload))
                                {
                                    upToDate = true;
                                }
                            }
                        }).ConfigureAwait(false);
                    if (upToDate)
                        break;
                    page++;
                } while (!upToDate && !neverDone);

                List<string> ids = newChaps.Keys.ToList();
                List<LatestSerieEntity> toUpdate = await _db.LatestSeries.Where(a => ids.Contains(a.MihonId)).ToListAsync(token).ConfigureAwait(false);
                List<(LatestSerieEntity, SeriesProviderEntity)> toCheck = [];

                foreach (ComboSeries c in newChaps.Values)
                {
                    LatestSerieEntity? s = toUpdate.FirstOrDefault(a => a.MihonId == c.MihonId);
                    if (s == null)
                    {
                        s = new LatestSerieEntity();
                        s.MihonId = c.MihonId;
                        s.MihonProviderId = mihonProviderId;
                        _db.LatestSeries.Add(s);
                    }
                    if (c.Series != null)
                    {
                        await s.PopulateSeriesAsync(src, c.Series, _cache).ConfigureAwait(false);
                    }
                    s.Chapters = c.Chapters;
                    ParsedChapter? latest_online = s.Chapters.OrderByDescending(a => a.Index).FirstOrDefault();
                    DateTime latestUTC = (latest_online?.DateUpload ?? DateTime.MinValue).DateTime;

                    if (latestUTC > DateTime.UtcNow || latestUTC.AddMonths(1) < DateTime.UtcNow)
                    {
                        latestUTC = DateTime.UtcNow;
                    }
                    s.FetchDate = latestUTC;
                    s.LatestChapter = latest_online?.ParsedNumber ?? -1.0m;
                    s.ChapterCount = s.Chapters.Count;
                    s.LatestChapterTitle = latest_online?.Name ?? "";
                    SeriesProviderEntity? serie = await _db.SeriesProviders
                        .Where(a => a.MihonId == s.MihonId).AsNoTracking()
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
                    Models.Database.SeriesEntity series = await _db.Series.Include(a => a.Sources)
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
            SeriesProviderEntity? serie = await _db.SeriesProviders.Where(s => s.Id == seriesProvider).AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
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
            if (string.IsNullOrEmpty(serie.MihonProviderId))
            {
                _logger.LogWarning("Series Provider {SeriesProvider} has no longer valid Mihon Id", seriesProvider);
                return JobResult.Failed;
            }
            var series = await _db.Series.Include(a => a.Sources).Where(s => s.Id == serie.SeriesId).AsNoTracking().FirstAsync(token).ConfigureAwait(false);
            List<ParsedChapter>? chapterData;

            ISourceInterop src;
            try
            {
                src = await _mihon.SourceFromProviderIdAsync(serie.MihonProviderId!, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to get Chapter from {mihonProviderId}", serie.MihonProviderId);
                return JobResult.Failed;
            }
            string provider = src.Name + " (" + src.Language + ")";
            chapterData = await _mihon.MihonErrorWrapperAsync(
                     () => src.GetChaptersAsync(serie.ToManga()!, token),
                     "Unable to get Chapters from {series} from {provider}", serie.Title, provider).ConfigureAwait(false);
            if (chapterData == null)
                return JobResult.Failed;
            if (chapterData.Count == 0)
            {
                _logger.LogWarning("Series {series} from Provider {provider} has no chapters.", serie.Title, provider);
                return JobResult.Failed;
            }
            List<ChapterDownload> chaps = series.GenerateDownloadsFromChapterData(serie, chapterData);
            return await _downloadCommand.QueueChapterDownloadsAsync(serie, chaps, token).ConfigureAwait(false);

        }
       // Private helper methods
        private async Task<Models.Database.SeriesEntity?> FindExistingSeriesAsync(AugmentedResponseDto ProviderSeriesDetails,
            SettingsDto settings, Dictionary<string, Guid> paths, CancellationToken token)
        {
            if (ProviderSeriesDetails.StorageFolderPath.StartsWith(settings.StorageFolder))
                ProviderSeriesDetails.StorageFolderPath = ProviderSeriesDetails.StorageFolderPath[settings.StorageFolder.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            ProviderSeriesDetails.StorageFolderPath = settings.StorageFolder.GetActualDirectoryPathCaseInsensitive(
                ProviderSeriesDetails.StorageFolderPath);

            if (paths.TryGetValue(ProviderSeriesDetails.StorageFolderPath, out Guid id))
            {
                return await _db.Series.FirstOrDefaultAsync(s => s.Id == id, token).ConfigureAwait(false);
            }

            // Search by title similarity
            var allProvs = await _db.SeriesProviders.Select(a => new { a.Title, a.SeriesId })
                .ToListAsync(token).ConfigureAwait(false);
            
            foreach (var n in allProvs)
            {
                foreach (var ser in ProviderSeriesDetails.Series)
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

        private List<SeriesProviderEntity> ProcessSeriesProviders(AugmentedResponseDto ProviderSeriesDetails, List<SeriesProviderEntity> existingProviders)
        {
            List<ImportProviderSnapshot> pInfos = ProviderSeriesDetails.LocalInfo?.Providers ?? [];

            foreach (var fs in ProviderSeriesDetails.Series)
            {
                ImportProviderSnapshot? pInfo = FindMatchingImportProviderSnapshot(pInfos, fs);
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
                    
                    InternalCreateOrUpdateProviderFromProviderSeriesDetails(fs, existingProvider);
                }
                else
                {
                    existingProvider = InternalCreateOrUpdateProviderFromProviderSeriesDetails(fs);
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
            foreach (ImportProviderSnapshot p in pInfos)
            {
                var nProvider = p.ToSeriesProvider();
                InternalAssignArchives(nProvider, p.Archives);
                _db.SeriesProviders.Add(nProvider);
                existingProviders.Add(nProvider);
            }

            return existingProviders;
        }

        private static ImportProviderSnapshot? FindMatchingImportProviderSnapshot(List<ImportProviderSnapshot> pInfos, ProviderSeriesDetails fs)
        {
            foreach (ImportProviderSnapshot p in pInfos)
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

        private static void UpdateProviderSettings(SeriesExtendedDto series, Models.Database.SeriesEntity dbSeries)
        {
            foreach (ProviderExtendedDto p in series.Providers)
            {
                SeriesProviderEntity? n = dbSeries.Sources.FirstOrDefault(a => a.Id == p.Id);
                if (n == null)
                    continue;
                
                n.IsDisabled = p.IsDisabled;
                n.IsStorage = p.IsStorage;
                n.IsTitle = p.UseTitle;
                n.IsCover = p.UseCover;
                n.IsLocal = p.IsLocal;
                n.ContinueAfterChapter = p.ContinueAfterChapter;
            }
        }

        private void InternalAssignArchives(SeriesProviderEntity provider, List<ProviderArchiveSnapshot>? archives)
        {
            provider.AssignArchives(archives);
            _db.Touch(provider, e => e.Chapters);
        }

        private SeriesProviderEntity InternalCreateOrUpdateProviderFromProviderSeriesDetails(ProviderSeriesDetails fs, SeriesProviderEntity? provider = null)
        {
            provider = fs.CreateOrUpdate(provider);
            _db.Touch(provider, e => e.Chapters);
            return provider;
        }

        private async Task<Models.Database.SeriesEntity> ConsolidateDBSeriesFromProvidersAsync(Models.Database.SeriesEntity? dbSeries,
            List<SeriesProviderEntity> providers, string path, bool startDisabled, decimal? startFromChapter, CancellationToken token = default)
        {
            var consolidatedSeries = providers.ToProviderSeriesDetails();
            
            if (dbSeries != null)
            {
                dbSeries.FillSeriesFromProviderSeriesDetails(consolidatedSeries, startFromChapter);
            }
            else
            {
                dbSeries = consolidatedSeries.ToSeries(path);
                dbSeries.PauseDownloads = startDisabled;
                dbSeries.StartFromChapter = startFromChapter;
                await _db.Series.AddAsync(dbSeries, token).ConfigureAwait(false);
            }

            return dbSeries;
        }
        private class ComboSeries
        {
            public string MihonId { get; set; }
            public ParsedManga? Series { get; set; }
            public List<ParsedChapter> Chapters { get; set; } = [];
        }

    }
}

