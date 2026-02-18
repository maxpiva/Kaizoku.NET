using System.Text.Json;
using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Naming;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Series
{
    /// <summary>
    /// Service responsible for provider matching and provider-related operations
    /// </summary>
    public class SeriesProviderService
    {
        private readonly AppDbContext _db;
        private readonly SettingsService _settings;
        private readonly JobBusinessService _jobBusinessService;
        private readonly JobManagementService _jobManagementService;
        private readonly ITemplateParser _templateParser;
        private readonly ILogger<SeriesProviderService> _logger;

        public SeriesProviderService(AppDbContext db, SettingsService settings, JobBusinessService jobBusinessService,
            JobManagementService jobManagementService, ITemplateParser templateParser, ILogger<SeriesProviderService> logger)
        {
            _db = db;
            _settings = settings;
            _jobBusinessService = jobBusinessService;
            _jobManagementService = jobManagementService;
            _templateParser = templateParser;
            _logger = logger;
        }

        /// <summary>
        /// Gets a provider match by provider ID for unknown providers
        /// </summary>
        /// <param name="providerId">The provider's unique identifier</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>The provider match if found</returns>
        public async Task<ProviderMatch?> GetMatchAsync(Guid providerId, CancellationToken token = default)
        {

            SeriesProvider? provider = await _db.SeriesProviders.Where(a => a.Id == providerId).AsNoTracking()
                .FirstOrDefaultAsync(token).ConfigureAwait(false);
            if (provider == null || (provider.SuwayomiId!=0 && !provider.IsUnknown))
                return null;
            
            List<SeriesProvider> providers = await _db.SeriesProviders
                .Where(a => a.SeriesId == provider.SeriesId && !a.IsUnknown && a.SuwayomiId!=0).AsNoTracking().ToListAsync(token)
                .ConfigureAwait(false);
            if (providers.Count == 0)
                return null;
            
            ProviderMatch m = new ProviderMatch
            {
                Id = provider.Id,
                MatchInfos = providers.Select(a => new MatchInfo
                    { Id = a.Id, Language = a.Language, Scanlator = a.Scanlator, Provider = a.Provider }).ToList(),
                Chapters = provider.Chapters
                    .Where(a => !a.IsDeleted && !string.IsNullOrEmpty(a.Filename))
                    .Select(c => new ProviderMatchChapter
                    {
                        ChapterNumber = c.Number,
                        ChapterName = c.Name ?? "",
                        MatchInfoId = null,
                        Filename = Path.GetFileNameWithoutExtension(c.Filename) ?? ""
                    }).OrderBy(a => a.ChapterNumber).ToList()
            };
            return m;
        }

        /// <summary>
        /// Sets a provider match by moving chapters from unknown provider to known providers
        /// </summary>
        /// <param name="pm">The provider match object</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>True if the match was set successfully</returns>
        public async Task<bool> SetMatchAsync(ProviderMatch pm, CancellationToken token = default)
        {
            Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            SeriesProvider? unknown = await _db.SeriesProviders.FirstOrDefaultAsync(a => a.Id == pm.Id, token)
                .ConfigureAwait(false);
            if (unknown == null)
                return false;
            
            Models.Database.Series? series = await _db.Series.Include(a => a.Sources)
                .FirstOrDefaultAsync(a => a.Id == unknown.SeriesId, token).ConfigureAwait(false);
            if (series == null)
                return false;
            
            Dictionary<Guid, SeriesProvider> minfo =
                pm.MatchInfos.ToDictionary(a => a.Id, a => series.Sources.First(b => b.Id == a.Id));
            
            bool update = false;
            foreach (ProviderMatchChapter chap in pm.Chapters)
            {
                if (chap.MatchInfoId == null)
                    continue;
                
                SeriesProvider mi = minfo[chap.MatchInfoId.Value];
                Chapter? ch = unknown.Chapters.FirstOrDefault(a => Path.GetFileNameWithoutExtension(a.Filename) == chap.Filename);
                Chapter? dst = mi.Chapters.FirstOrDefault(a => a.Number == chap.ChapterNumber);
                
                if (ch != null && dst != null)
                {
                    decimal? maxChap = mi.Chapters.Max(c => c.Number);

                    // Use template parser for consistent filename generation
                    var vars = new TemplateVariables(
                        Series: mi.Title,
                        Chapter: dst.Number,
                        Volume: null,
                        Provider: mi.Provider,
                        Scanlator: mi.Scanlator,
                        Language: mi.Language,
                        Title: settings.IncludeChapterTitle ? dst.Name : null,
                        UploadDate: dst.ProviderUploadDate,
                        Type: series.Type,
                        MaxChapter: maxChap
                    );
                    string filename = _templateParser.ParseFileName(settings.FileNameTemplate, vars, settings);

                    string? extension = Path.GetExtension(ch.Filename);
                    string newFilename = filename + extension;
                    string originalPath = Path.Combine(settings.StorageFolder, series.StoragePath, ch.Filename ?? "");
                    string newPath = Path.Combine(settings.StorageFolder, series.StoragePath, newFilename);
                    
                    if (File.Exists(originalPath))
                    {
                        try
                        {
                            if (originalPath != newPath)
                                File.Move(originalPath, newPath, true);
                            _db.Touch(mi, a => a.Chapters);
                            dst.Filename = newFilename;
                            dst.DownloadDate = ch.DownloadDate;
                            dst.ShouldDownload = false;
                            update = true;
                            unknown.Chapters.Remove(ch);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Error renaming file from {originalPath} to {newPath}", originalPath, newPath);
                        }
                    }
                    else
                    {
                        update = true;
                        unknown.Chapters.Remove(ch);
                    }
                }
            }

            if (unknown.Chapters.Count == 0)
            {
                series.Sources.Remove(series.Sources.First(a => a.Id == unknown.Id));
                _db.SeriesProviders.Remove(unknown);
            }
            else if (update)
            {
                _db.Touch(unknown, c => c.Chapters);
            }

            if (update)
            {
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                await series.SaveKaizokuInfoToDirectoryAsync(Path.Combine(settings.StorageFolder, series.StoragePath),
                    _logger, token);
            }

            return true;
        }

        /// <summary>
        /// Reschedules jobs for providers if needed
        /// </summary>
        /// <param name="providers">Collection of providers to reschedule</param>
        /// <param name="immediate">Whether to run immediately</param>
        /// <param name="forceDisable">Whether to force disable</param>
        /// <param name="token">Cancellation token</param>
        public async Task RescheduleIfNeededAsync(IEnumerable<SeriesProvider> providers, bool immediate = true,
            bool forceDisable = false, CancellationToken token = default)
        {
            foreach (SeriesProvider p in providers.Where(a => !a.IsUnknown))
            {
                await _jobBusinessService.ManageSeriesProviderJobAsync(p, immediate, forceDisable, token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates the in-library status of series in the latest series table
        /// </summary>
        /// <param name="providers">Collection of providers</param>
        /// <param name="deletedIds">Collection of deleted series IDs</param>
        /// <param name="token">Cancellation token</param>
        public async Task CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
            IEnumerable<SeriesProvider> providers, IEnumerable<int> deletedIds, CancellationToken token = default)
        {
            List<int> ids = providers.Select(a => a.SuwayomiId).Union(deletedIds).ToList();
            List<LatestSerie> latest = await _db.LatestSeries.Where(a => ids.Contains(a.SuwayomiId)).ToListAsync(token)
                .ConfigureAwait(false);
            
            foreach (LatestSerie l in latest)
            {
                if (deletedIds.Contains(l.SuwayomiId))
                {
                    l.InLibrary = InLibraryStatus.NotInLibrary;
                }
                else
                {
                    SeriesProvider? sp = providers.First(a => a.SuwayomiId == l.SuwayomiId);
                    InLibraryStatus status = InLibraryStatus.InLibrary;
                    if (sp.IsUninstalled || sp.IsDisabled)
                        status = InLibraryStatus.InLibraryButDisabled;
                    l.InLibrary = status;
                }
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes sources from a series if they are marked for deletion
        /// </summary>
        /// <param name="series">Series extended info with provider updates</param>
        /// <param name="dbSeries">Database series entity</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of deleted source IDs</returns>
        public async Task<List<int>> DeleteSourcesIfNeededAsync(SeriesExtendedInfo series, Models.Database.Series dbSeries,
            CancellationToken token = default)
        {
            List<int> deletes = new List<int>();
            foreach (ProviderExtendedInfo p in series.Providers)
            {
                SeriesProvider toBeDeleted = dbSeries.Sources.First(a => a.Id == p.Id);
                if (toBeDeleted.IsUnknown && toBeDeleted.Chapters.All(a => a.Filename == null))
                    p.IsDeleted = true;
                
                if (p.IsDeleted)
                {
                    string provider = toBeDeleted.Provider;
                    string scanlator = toBeDeleted.Scanlator;

                    List<Chapter> chapters = toBeDeleted.Chapters.Where(a => !string.IsNullOrEmpty(a.Filename)).ToList();
                    if (chapters.Count > 0)
                    {
                        SeriesProvider? unknown = dbSeries.Sources.FirstOrDefault(a => a.Provider == "Unknown");
                        if (unknown != null)
                        {
                            unknown.Chapters.AddRange(chapters);
                            unknown.Chapters = unknown.Chapters.OrderBy(a => a.Number).ToList();
                            _db.Touch(unknown, a => a.Chapters);
                            dbSeries.Sources.Remove(toBeDeleted);
                            _db.SeriesProviders.Remove(toBeDeleted);
                        }
                        else
                        {
                            // Convert provider to unknown
                            toBeDeleted.SuwayomiId = 0;
                            toBeDeleted.Provider = "Unknown";
                            toBeDeleted.Scanlator = string.Empty;
                            toBeDeleted.Url = string.Empty;
                            toBeDeleted.ThumbnailUrl = string.Empty;
                            toBeDeleted.FetchDate = chapters.Max(a => a.ProviderUploadDate);
                            toBeDeleted.IsUnknown = true;
                            toBeDeleted.ContinueAfterChapter = toBeDeleted.Chapters.Max(a => a.Number);
                            toBeDeleted.IsTitle = false;
                            toBeDeleted.IsCover = false;
                            toBeDeleted.IsDisabled = false;
                            toBeDeleted.Status = SeriesStatus.UNKNOWN;
                        }
                    }
                    else
                    {
                        dbSeries.Sources.Remove(toBeDeleted);
                        _db.SeriesProviders.Remove(toBeDeleted);
                        deletes.Add(toBeDeleted.SuwayomiId);
                    }

                    // Cleanup downloads
                    await CleanupDownloadsAsync(provider, scanlator, dbSeries, p.Id, token);
                }
            }

            return deletes;
        }

        /// <summary>
        /// Cleans up download jobs for a deleted provider
        /// </summary>
        private async Task CleanupDownloadsAsync(string provider, string scanlator, Models.Database.Series dbSeries, 
            Guid providerId, CancellationToken token)
        {
            List<Enqueue> queues = await _db.Queues
                .Where(a => a.JobType == JobType.Download &&
                            a.ExtraKey == dbSeries.Id.ToString().ToLowerInvariant()).AsNoTracking()
                .ToListAsync(token).ConfigureAwait(false);
            
            List<Guid> toBeDeleted = [];
            foreach (Enqueue q in queues)
            {
                if (string.IsNullOrEmpty(q.JobParameters))
                    continue;
                
                ChapterDownload? chap = JsonSerializer.Deserialize<ChapterDownload>(q.JobParameters);
                if (chap == null)
                    continue;
                
                if (chap.ProviderName == provider && chap.Scanlator == scanlator)
                {
                    toBeDeleted.Add(q.Id);
                }
            }

            await _jobBusinessService.DeleteSeriesProviderJobAsync(new SeriesProvider { Id = providerId }, token);
            if (toBeDeleted.Count > 0)
                await _jobManagementService.DeleteQueuedJobsAsync(toBeDeleted, token).ConfigureAwait(false);
        }
    }
}