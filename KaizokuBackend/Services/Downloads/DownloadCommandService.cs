using KaizokuBackend.Data;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Jobs.Report;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Utils;
using Microsoft.EntityFrameworkCore;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using System.Net;
using System.Text.Json;
using KaizokuBackend.Extensions;
using KaizokuBackend.Services.Settings;

namespace KaizokuBackend.Services.Downloads
{
    /// <summary>
    /// Service for download command operations following CQRS pattern
    /// </summary>
    public class DownloadCommandService
    {
        private readonly SuwayomiClient _suwayomi;
        private readonly AppDbContext _db;
        private readonly SettingsService _settings;
        private readonly JobManagementService _jobManagementService;
        private readonly JobHubReportService _reportingService;
        private readonly string _tempFolder;
        private readonly ILogger<DownloadCommandService> _logger;
        private static readonly KeyedAsyncLock _lock = new KeyedAsyncLock();

        public DownloadCommandService(
            SuwayomiClient suwayomi,
            AppDbContext db,
            SettingsService settings,
            JobManagementService jobManagementService,
            JobHubReportService reportingService,
            IConfiguration config,
            ILogger<DownloadCommandService> logger)
        {
            _suwayomi = suwayomi;
            _db = db;
            _settings = settings;
            _jobManagementService = jobManagementService;
            _reportingService = reportingService;
            _logger = logger;
            _tempFolder = Path.Combine(config["runtimeDirectory"] ?? "", "Downloads");
        }

        /// <summary>
        /// Downloads a chapter and saves it as a CBZ file
        /// </summary>
        /// <param name="ch">Chapter download information</param>
        /// <param name="job">Job information for progress reporting</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Job result indicating success or failure</returns>
        public async Task<JobResult> DownloadChapterAsync(ChapterDownload ch, JobInfo job, CancellationToken token = default)
        {
            ProgressReporter reporter = _reportingService.CreateReporter(job);
            DownloadCardInfo dci = new DownloadCardInfo
            {
                Scanlator = ch.Scanlator,
                Provider = ch.ProviderName,
                ChapterNumber = ch.Chapter.ChapterNumber,
                Title = ch.Title,
                Language = ch.Language,
                PageCount = ch.PageCount,
                ChapterName = ch.ChapterName,
                ThumbnailUrl = ch.ThumbnailUrl
            };

            var appSettings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            
            if (!ch.ChapterLoaded)
            {
                SuwayomiChapter? n = await _suwayomi.GetChapterAsync(ch.SuwayomiId, ch.SuwayomiIndex, token).ConfigureAwait(false);
                if (n == null)
                {
                    _logger.LogError("Error downloading chapter {ChapterNumber} of series {SeriesTitle}",
                        ch.Chapter.ChapterNumber, ch.Title);
                    return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
                }

                ch.Chapter = n;
                ch.PageCount = n.PageCount;
                ch.Scanlator = n.Scanlator;
                ch.ChapterName = n.Name;
            }

            if (ch.PageCount < 0)
                ch.PageCount = 50;

            string providerName = ch.ProviderName;
            if (ch.Scanlator != null)
                providerName += "-" + ch.Scanlator;

            string chapterName = "";
            if (ch.Chapter.ChapterNumber.HasValue)
                chapterName = $"chapter {ch.Chapter.ChapterNumber.Value.FormatDecimal()} ";

            string? rchap = null;
            if (!string.IsNullOrEmpty(ch.ChapterName))
            {
                string cc = ch.ChapterName.Trim().ToLowerInvariant();
                if (!cc.Contains("ch.") && !cc.Contains("chapter"))
                    rchap = ch.ChapterName.Trim();
            }

            decimal? maxChap = null;
            SeriesProvider? p = await _db.SeriesProviders.Where(a => a.Id == ch.SeriesProviderId).AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
            if (p != null)
                maxChap = p.Chapters.Max(c => c.Number);

            string zipFile = ArchiveHelperService.MakeFileNameSafe(ch.ProviderName, ch.Scanlator, ch.SeriesTitle, ch.Language, ch.Chapter.ChapterNumber, rchap, maxChap) + ".cbz";
            string message = $"Downloading ({providerName}) {ch.Title} {chapterName}...";
            reporter.Report(ProgressStatus.Started, 0, message, dci);

            float step = 100 / (float)(ch.PageCount);
            float acum = 0;
            int page = 0;
            string tempZipPath = Path.Combine(_tempFolder, zipFile);
            bool breaked = false;

            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(_tempFolder))
                        Directory.CreateDirectory(_tempFolder);
                }

                if (File.Exists(tempZipPath))
                    File.Delete(tempZipPath);

                using (var zipStream = File.OpenWrite(tempZipPath))
                using (var zipWriter = WriterFactory.Open(zipStream, ArchiveType.Zip, CompressionType.None))
                {
                    while (true)
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            try
                            {
                                (HttpStatusCode code, Stream? image) = await _suwayomi.GetPageAsync(ch.SuwayomiId, ch.SuwayomiIndex, page, token).ConfigureAwait(false);
                                if (code == HttpStatusCode.NotFound)
                                    break;

                                if (image == null)
                                {
                                    _logger.LogWarning("Failed to download page {Page} for chapter {ChapterNumber} of series {SeriesTitle}",
                                        page, ch.Chapter.ChapterNumber, ch.Title);
                                    breaked = true;
                                    break;
                                }

                                await image.CopyToAsync(ms, token).ConfigureAwait(false);
                                image.Close();
                                await image.DisposeAsync().ConfigureAwait(false);
                                ms.Position = 0;

                                (_, string? ext) = ms.GetImageMimeTypeAndExtension();
                                if (ext == null)
                                {
                                    _logger.LogWarning("Page {Page+1} of chapter {ChapterNumber} of series {SeriesTitle} is not a valid image", page, ch.Chapter.ChapterNumber, ch.Title);
                                    ext = ".unk";
                                    break;
                                }

                                string fileName = ArchiveHelperService.MakeFileNameSafe(ch.ProviderName, ch.Scanlator, ch.SeriesTitle, ch.Language,
                                    ch.Chapter.ChapterNumber, ch.ChapterName, maxChap, page + 1, ch.PageCount) + ext;
                                zipWriter.Write(fileName, ms);
                                page++;
                                acum += step;
                                message = $"Downloading ({providerName}) {ch.Title} {chapterName} {page}";
                                reporter.Report(ProgressStatus.InProgress, (int)acum, message, dci);
                            }
                            catch (Exception)
                            {
                                _logger.LogError("Failed to download page {Page} for chapter {ChapterNumber} of series {SeriesTitle}",
                                    page, ch.Chapter.ChapterNumber, ch.Title);
                                breaked = true;
                                break;
                            }
                        }

                        if (breaked)
                            break;
                    }

                    if (page == 0)
                    {
                        _logger.LogError("Failed to download page {Page} for chapter {ChapterNumber} of series {SeriesTitle} [No Pages]", page, ch.Chapter.ChapterNumber, ch.Title);
                        breaked = true;
                    }

                    if (!breaked)
                    {
                        using (Stream comicInfo = ArchiveHelperService.CreateComicInfo(ch, page).ToStream())
                        {
                            ((ZipWriter)zipWriter).Write("ComicInfo.xml", comicInfo, new ZipWriterEntryOptions { CompressionType = CompressionType.Deflate, ModificationDateTime = DateTime.Now });
                        }
                    }
                }

                if (breaked)
                {
                    try
                    {
                        File.Delete(tempZipPath);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to delete temporary zip file {TempZipPath}", tempZipPath);
                    }
                    reporter.Report(ProgressStatus.Failed, (int)acum, message, dci);
                    return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
                }

                string dirPath = Path.Combine(appSettings.StorageFolder, ch.StoragePath);
                if (!Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);

                string finalPath = Path.Combine(dirPath, zipFile);
                try
                {
                    await Task.Run(() => File.Move(tempZipPath, finalPath, true), token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to move downloaded file from {TempZipPath} to {FinalPath}", tempZipPath, finalPath);
                    reporter.Report(ProgressStatus.Failed, (int)acum, message, dci);
                    return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
                }

                using (var n = await _lock.LockAsync(ch.SeriesId.ToString(), token).ConfigureAwait(false))
                {
                    SeriesProvider? provider = await _db.SeriesProviders.FirstOrDefaultAsync(a => a.Id == ch.SeriesProviderId, token).ConfigureAwait(false);
                    if (provider == null)
                    {
                        _logger.LogWarning("Series Provider {ProviderName} no longer exists.", ch.ProviderName);
                        reporter.Report(ProgressStatus.Completed, 100, "", dci);
                        return JobResult.Failed;
                    }

                    Chapter? cha = provider.Chapters.FirstOrDefault(c => c.Number == ch.Chapter.ChapterNumber);
                    if (cha == null)
                    {
                        cha = new Chapter();
                        provider.Chapters.Add(cha);
                        provider.Chapters = provider.Chapters.OrderBy(c => c.Number).ToList();
                    }

                    cha.PageCount = page;
                    cha.IsDeleted = false;
                    cha.Name = ch.Chapter.Name;
                    cha.Number = ch.Chapter.ChapterNumber;
                    cha.DownloadDate = DateTime.UtcNow;
                    cha.ProviderUploadDate = ch.ComicUploadDateUTC;
                    cha.Filename = zipFile;
                    cha.ShouldDownload = false;
                    provider.ContinueAfterChapter = provider.Chapters.MaxNull(c => c.Number);
                    provider.ChapterCount = provider.Chapters.Count;
                    _db.Touch(provider, a => a.Chapters);
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);

                    Models.Database.Series s = await _db.Series.Include(a => a.Sources).Where(a => a.Id == provider.SeriesId).FirstAsync(token);
                    if (provider.IsStorage)
                    {
                        List<Chapter> chapters = s.Sources.Where(a => !a.IsDisabled && !a.IsUninstalled && !a.IsStorage)
                            .SelectMany(a => a.Chapters).Where(c => c.Number == ch.Chapter.ChapterNumber && !string.IsNullOrEmpty(c.Filename)).ToList();
                        if (chapters.Count > 0)
                        {
                            //Delete temporary sources chapters if needed, since we have the storage one
                            foreach (Chapter c in chapters)
                            {
                                string rfname = Path.Combine(appSettings.StorageFolder, s.StoragePath, c.Filename!);
                                if (File.Exists(rfname))
                                {
                                    try
                                    {
                                        File.Delete(rfname);
                                    }
                                    catch
                                    {
                                        _logger.LogError("Unable to delete file {rfname}", rfname);
                                    }
                                }
                                c.Filename = string.Empty;
                                c.IsDeleted = true;
                            }
                        }
                        await _db.SaveChangesAsync(token).ConfigureAwait(false);
                    }

                    string fullPath = Path.Combine(appSettings.StorageFolder, s.StoragePath);
                    await s.SaveKaizokuInfoToDirectoryAsync(fullPath, _logger, token).ConfigureAwait(false);
                }

                message = $"Downloading ({providerName}) {ch.Title} {chapterName} completed.";
                reporter.Report(ProgressStatus.Completed, 100, message, dci);
                return JobResult.Success;
            }
            catch (Exception e)
            {
                if (File.Exists(tempZipPath))
                {
                    try
                    {
                        File.Delete(tempZipPath);
                    }
                    catch
                    {
                    }
                }
                _logger.LogError(e, "Error downloading chapter {ChapterNumber} of series {SeriesTitle}: {Message}", ch.Chapter.ChapterNumber, ch.Title, e.Message);
                reporter.Report(ProgressStatus.Failed, (int)100, "Error downloading chapter", dci);
                return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Manages error downloads by retrying or deleting them
        /// </summary>
        /// <param name="id">Download ID</param>
        /// <param name="action">Action to take</param>
        /// <param name="token">Cancellation token</param>
        public async Task ManageErrorDownloadAsync(Guid id, ErrorDownloadAction action, CancellationToken token = default)
        {
            Enqueue? d = await _db.Queues.Where(a => a.Id == id && a.JobType == JobType.Download).AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
            if (d == null)
                return;

            if (action == ErrorDownloadAction.Retry)
            {
                if (string.IsNullOrEmpty(d.JobParameters))
                    return;
                ChapterDownload? ch = JsonSerializer.Deserialize<ChapterDownload>(d.JobParameters);
                if (ch == null)
                    return;
                ch.Retries = 0;
                await RescheduleDownloadAsync(ch, token);
                return;
            }

            if (action == ErrorDownloadAction.Delete)
            {
                Enqueue delete = await _db.Queues.FirstAsync(a => a.Id == id, token).ConfigureAwait(false);
                _db.Queues.Remove(delete);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Queues chapter downloads for a series provider
        /// </summary>
        /// <param name="serie">Series provider</param>
        /// <param name="chaps">Chapter downloads to queue</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Job result</returns>
        public async Task<JobResult> QueueChapterDownloadsAsync(SeriesProvider serie, List<ChapterDownload> chaps, CancellationToken token = default)
        {
            string scanlator = string.Empty;
            if (!string.IsNullOrEmpty(serie.Scanlator) && serie.Scanlator != serie.Provider)
                scanlator = ":" + serie.Scanlator;

            if (chaps.Count == 0)
                _logger.LogInformation("Provider {Provider}:{Lang}{scanlator} does not have new Chapters for Series '{Title}'.", serie.Provider, serie.Language, scanlator, serie.Title);
            else
            {
                int updateCount = chaps.Count(a => a.IsUpdate);
                int newCount = chaps.Count - updateCount;
                if (updateCount > 0 && newCount > 0)
                {
                    _logger.LogInformation("Provider {Provider}:{Lang}{scanlator} has {newCount} new Chapters and {updateCount} updated Chapters for Series '{Title}'.", serie.Provider, serie.Language, scanlator, newCount, updateCount, serie.Title);
                }
                else if (updateCount > 0)
                {
                    _logger.LogInformation("Provider {Provider}:{Lang}{scanlator} has {updateCount} updated Chapters for Series '{Title}'.", serie.Provider, serie.Language, scanlator, updateCount, serie.Title);
                }
                else
                {
                    _logger.LogInformation("Provider {Provider}:{Lang}{scanlator} has {newCount} new Chapters for Series '{Title}'.", serie.Provider, serie.Language, scanlator, newCount, serie.Title);
                }

                foreach (ChapterDownload ch in chaps.OrderBy(a => a.SuwayomiIndex))
                {
                    string key = $"{ch.SeriesProviderId}|{ch.SuwayomiId}|{ch.SuwayomiIndex}";
                    string groupKey = $"{ch.ProviderName}";
                    await _jobManagementService.EnqueueJobAsync(JobType.Download, ch, Priority.Normal, key, groupKey, ch.SeriesId.ToString(), "Downloads", token).ConfigureAwait(false);
                }
            }
            return JobResult.Success;
        }

        /// <summary>
        /// Reschedules a failed download with retry logic
        /// </summary>
        /// <param name="download">Chapter download to reschedule</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Job result</returns>
        private async Task<JobResult> RescheduleDownloadAsync(ChapterDownload download, CancellationToken token = default)
        {
            KaizokuBackend.Models.Settings appSettings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            download.Retries++;
            string key = $"{download.SeriesProviderId}|{download.SuwayomiId}|{download.SuwayomiIndex}";
            
            if (download.Retries > appSettings.ChapterDownloadFailRetries)
            {
                _logger.LogWarning("Max retries reached for chapter {ChapterNumber} of series {SeriesTitle} from {ProviderName}. Giving up.", download.Chapter.ChapterNumber, download.Title, download.ProviderName);
                return JobResult.Failed;
            }
            
            string groupKey = $"{download.SeriesProviderId}|{download.SuwayomiId}";
            DateTime nextTime = DateTime.UtcNow.Add(appSettings.ChapterDownloadFailRetryTime);
            await _jobManagementService.ScheduleJobAsync(JobType.Download, download, nextTime, "Downloads", key, groupKey, download.SeriesId.ToString(), Priority.Normal, download.Retries, token).ConfigureAwait(false);
            return JobResult.Handled;
        }
    }
}