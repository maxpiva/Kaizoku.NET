using System.Text.Json;
using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Helpers;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace RensaioBackend.Services.Series
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
        private readonly ILogger<SeriesProviderService> _logger;
        private readonly SeriesStateService _stateService;

        public SeriesProviderService(AppDbContext db, SettingsService settings, JobBusinessService jobBusinessService,
            JobManagementService jobManagementService, ILogger<SeriesProviderService> logger,
            SeriesStateService stateService)
        {
            _db = db;
            _settings = settings;
            _jobBusinessService = jobBusinessService;
            _jobManagementService = jobManagementService;
            _logger = logger;
            _stateService = stateService;
        }

        /// <summary>
        /// Gets a provider match by provider ID for unknown providers.
        /// Returns both Mihon-linked and user-based providers as potential match targets.
        /// </summary>
        /// <param name="providerId">The provider's unique identifier</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>The provider match if found</returns>
        public async Task<ProviderMatchDto?> GetMatchAsync(Guid providerId, CancellationToken token = default)
        {

            SeriesProviderEntity? provider = await _db.SeriesProviders.Where(a => a.Id == providerId).AsNoTracking()
                .FirstOrDefaultAsync(token).ConfigureAwait(false);
            if (provider == null)
                return null;
            
            // Include ALL non-unknown providers on the same series (both Mihon-linked and user-based)
            List<SeriesProviderEntity> providers = await _db.SeriesProviders
                .Where(a => a.SeriesId == provider.SeriesId && !a.IsUnknown).AsNoTracking().ToListAsync(token)
                .ConfigureAwait(false);
            
            ProviderMatchDto m = new ProviderMatchDto
            {
                Id = provider.Id,
                MatchInfos = providers.Select(a => new MatchInfoDto
                    { Id = a.Id, Language = a.Language, Scanlator = a.Scanlator, Provider = a.Provider }).ToList(),
                Chapters = provider.Chapters
                    .Where(a => !a.IsDeleted && !string.IsNullOrEmpty(a.Filename))
                    .Select(c => new ProviderMatchChapterDto
                    {
                        ChapterNumber = c.Number,
                        ChapterName = c.Name ?? "",
                        MatchInfoId = null,
                        Filename = Path.GetFileNameWithoutExtension(c.Filename) ?? ""
                    }).OrderBy(a => a.ChapterNumber).ToList()
            };
            return m;
        }

        private static readonly Guid NewProviderSentinel = Guid.Empty;

        /// <summary>
        /// Sets a provider match by moving chapters from unknown provider to known providers.
        /// Supports both existing providers and creating new user-based providers.
        /// When a MatchInfo has Id == Guid.Empty, a new user-based provider is created
        /// using the MatchInfo's provider/scanlator/language metadata.
        /// </summary>
        /// <param name="pm">The provider match object</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>True if the match was set successfully</returns>
        public async Task<bool> SetMatchAsync(ProviderMatchDto pm, CancellationToken token = default)
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            SeriesProviderEntity? unknown = await _db.SeriesProviders.FirstOrDefaultAsync(a => a.Id == pm.Id, token)
                .ConfigureAwait(false);
            if (unknown == null)
                return false;
            
            Models.Database.SeriesEntity? series = await _db.Series.Include(a => a.Sources)
                .FirstOrDefaultAsync(a => a.Id == unknown.SeriesId, token).ConfigureAwait(false);
            if (series == null)
                return false;
            
            // Build dictionary of existing providers, excluding any sentinel entries
            Dictionary<Guid, SeriesProviderEntity> minfo = new();
            List<MatchInfoDto> newProviderInfos = new();
            foreach (MatchInfoDto matchInfo in pm.MatchInfos)
            {
                if (matchInfo.Id == NewProviderSentinel)
                {
                    newProviderInfos.Add(matchInfo);
                }
                else
                {
                    minfo[matchInfo.Id] = series.Sources.First(b => b.Id == matchInfo.Id);
                }
            }
            
            bool update = false;
            SeriesProviderEntity? newProvider = null;
            foreach (ProviderMatchChapterDto chap in pm.Chapters)
            {
                if (chap.MatchInfoId == null)
                    continue;
                
                SeriesProviderEntity? targetProvider;
                bool isNewProvider = false;
                
                if (chap.MatchInfoId.Value == NewProviderSentinel)
                {
                    // Use the first new provider info to create the user-based provider
                    MatchInfoDto? newProviderInfo = newProviderInfos.FirstOrDefault();
                    if (newProviderInfo == null)
                        continue;
                    
                    if (newProvider == null)
                    {
                        newProvider = SeriesProviderEntity.CreateUserBased(
                            newProviderInfo.Provider,
                            newProviderInfo.Scanlator,
                            newProviderInfo.Language
                        );
                        newProvider.SeriesId = series.Id;
                        series.Sources.Add(newProvider);
                        _db.SeriesProviders.Add(newProvider);
                    }
                    
                    targetProvider = newProvider;
                    isNewProvider = true;
                }
                else
                {
                    if (!minfo.TryGetValue(chap.MatchInfoId.Value, out targetProvider))
                        continue;
                }
                
                Chapter? ch = unknown.Chapters.FirstOrDefault(a => Path.GetFileNameWithoutExtension(a.Filename) == chap.Filename);
                if (ch == null)
                    continue;
                
                if (isNewProvider)
                {
                    // For a new provider, add the chapter directly
                    targetProvider.Chapters.Add(ch);
                    
                    // Rename file to match the target provider naming convention
                    string? extension = Path.GetExtension(ch.Filename);
                    decimal? maxChap = targetProvider.Chapters.Max(c => c.Number);
                    string filename = ArchiveHelperService.MakeFileNameSafe(targetProvider.Provider, targetProvider.Scanlator,
                        series.Title, targetProvider.Language, ch.Number, ch.Name, maxChap);
                    string newFilename = filename + extension;
                    string originalPath = Path.Combine(settings.StorageFolder, series.StoragePath, ch.Filename ?? "");
                    string newPath = Path.Combine(settings.StorageFolder, series.StoragePath, newFilename);
                    
                    if (File.Exists(originalPath))
                    {
                        try
                        {
                            if (originalPath != newPath)
                                File.Move(originalPath, newPath, true);
                            ch.Filename = newFilename;
                            ch.ShouldDownload = false;
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Error renaming file from {originalPath} to {newPath}", originalPath, newPath);
                        }
                    }
                }
                else
                {
                    // For existing providers, follow the original pattern
                    Chapter? dst = targetProvider.Chapters.FirstOrDefault(a => a.Number == chap.ChapterNumber);
                    if (ch != null && dst != null)
                    {
                        decimal? maxChap = targetProvider.Chapters.Max(c => c.Number);
                        string filename = ArchiveHelperService.MakeFileNameSafe(targetProvider.Provider, targetProvider.Scanlator,
                            series.Title, targetProvider.Language, dst.Number, dst.Name, maxChap);
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
                                _db.Touch(targetProvider, a => a.Chapters);
                                dst.Filename = newFilename;
                                dst.DownloadDate = ch.DownloadDate;
                                dst.ShouldDownload = false;
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e, "Error renaming file from {originalPath} to {newPath}", originalPath, newPath);
                            }
                        }
                    }
                }
                
                unknown.Chapters.Remove(ch);
                update = true;
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
                await _stateService.SyncToRensaioJsonAsync(series.Id, token).ConfigureAwait(false);
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
        public async Task RescheduleIfNeededAsync(IEnumerable<SeriesProviderEntity> providers, bool immediate = true,
            bool forceDisable = false, CancellationToken token = default)
        {
            foreach (SeriesProviderEntity p in providers.Where(a => !a.IsUnknown && !a.IsLocal))
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
            IEnumerable<SeriesProviderEntity> providers, IEnumerable<string> deletedIds, CancellationToken token = default)
        {
            var deletedList = deletedIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            List<string> ids = providers
                .Where(a => !string.IsNullOrWhiteSpace(a.MihonId))
                .Select(a => a.MihonId!)
                .Union(deletedList)
                .ToList();

            if (ids.Count == 0)
            {
                return;
            }

            List<LatestSerieEntity> latest = await _db.LatestSeries
                .Where(a => ids.Contains(a.MihonId!))
                .ToListAsync(token)
                .ConfigureAwait(false);
            
            foreach (LatestSerieEntity l in latest)
            {
                if (deletedList.Contains(l.MihonId))
                {
                    l.InLibrary = InLibraryStatus.NotInLibrary;
                }
                else
                {
                    SeriesProviderEntity? sp = providers.First(a => a.MihonId == l.MihonId);
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
        public async Task<List<string>> DeleteSourcesIfNeededAsync(SeriesExtendedDto series, Models.Database.SeriesEntity dbSeries,
            CancellationToken token = default)
        {
            List<string> deletes = new List<string>();
            foreach (ProviderExtendedDto p in series.Providers)
            {
                SeriesProviderEntity toBeDeleted = dbSeries.Sources.First(a => a.Id == p.Id);
                if (toBeDeleted.IsUnknown && toBeDeleted.Chapters.All(a => a.Filename == null))
                    p.IsDeleted = true;
                
                if (p.IsDeleted)
                {
                    string provider = toBeDeleted.Provider;
                    string scanlator = toBeDeleted.Scanlator;

                    List<Chapter> chapters = toBeDeleted.Chapters.Where(a => !string.IsNullOrEmpty(a.Filename)).ToList();
                    if (chapters.Count > 0)
                    {
                        SeriesProviderEntity? unknown = dbSeries.Sources.FirstOrDefault(a => a.Provider == "Unknown");
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
                            toBeDeleted.MihonId = string.Empty;
                            toBeDeleted.MihonProviderId = string.Empty;
                            toBeDeleted.BridgeItemInfo = string.Empty;
                            toBeDeleted.Provider = "Unknown";
                            toBeDeleted.Scanlator = string.Empty;
                            toBeDeleted.Url = string.Empty;
                            toBeDeleted.ThumbnailUrl = string.Empty;
                            toBeDeleted.FetchDate = chapters.Max(a => a.ProviderUploadDate);
                            toBeDeleted.IsUnknown = true;
                            toBeDeleted.ContinueAfterChapter = toBeDeleted.Chapters.Max(a => a.Number);
                            toBeDeleted.IsTitle = false;
                            toBeDeleted.IsCover = false;
                            toBeDeleted.IsLocal = true;
                            toBeDeleted.IsDisabled = false;
                            toBeDeleted.Status = SeriesStatus.UNKNOWN;
                        }
                    }
                    else
                    {
                        dbSeries.Sources.Remove(toBeDeleted);
                        _db.SeriesProviders.Remove(toBeDeleted);
                        if (!string.IsNullOrWhiteSpace(toBeDeleted.MihonId))
                        {
                            deletes.Add(toBeDeleted.MihonId);
                        }
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
        private async Task CleanupDownloadsAsync(string provider, string scanlator, Models.Database.SeriesEntity dbSeries, 
            Guid providerId, CancellationToken token)
        {
            List<EnqueueEntity> queues = await _db.Queues
                .Where(a => a.JobType == JobType.Download &&
                            a.ExtraKey == dbSeries.Id.ToString().ToLowerInvariant()).AsNoTracking()
                .ToListAsync(token).ConfigureAwait(false);
            
            List<Guid> toBeDeleted = [];
            foreach (EnqueueEntity q in queues)
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

            await _jobBusinessService.DeleteSeriesProviderJobAsync(new SeriesProviderEntity { Id = providerId }, token);
            if (toBeDeleted.Count > 0)
                await _jobManagementService.DeleteQueuedJobsAsync(toBeDeleted, token).ConfigureAwait(false);
        }
    }
}
