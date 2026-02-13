using System.Text.Json;
using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Jobs.Report;
using KaizokuBackend.Services.Settings;
using KaizokuBackend.Utils;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using ExtensionChapter = Mihon.ExtensionsBridge.Models.Extensions.Chapter;

namespace KaizokuBackend.Services.Downloads
{
    /// <summary>
    /// Service for download command operations following CQRS pattern
    /// </summary>
    public class DownloadCommandService
    {
        private readonly MihonBridgeService _mihon;
        private readonly AppDbContext _db;
        private readonly SettingsService _settings;
        private readonly JobManagementService _jobManagementService;
        private readonly JobHubReportService _reportingService;
        private readonly string _tempFolder;
        private readonly ILogger<DownloadCommandService> _logger;
        private static readonly KeyedAsyncLock _lock = new KeyedAsyncLock();

        public DownloadCommandService(
            MihonBridgeService mihon,
            AppDbContext db,
            SettingsService settings,
            JobManagementService jobManagementService,
            JobHubReportService reportingService,
            IConfiguration config,
            ILogger<DownloadCommandService> logger)
        {
            _mihon = mihon;
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
            DownloadSummary downloadSummary;

            var appSettings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            ISourceInterop src;
            try
            {
                src = await _mihon.SourceFromProviderIdAsync(ch.MihonProviderId, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to get DownloadChapter from {mihonProviderId}", ch.MihonProviderId);
                return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
            }
            if (src==null)
            {
                _logger.LogError("Source for provider ID {ProviderId} not found when downloading chapter {ChapterNumber} of series {SeriesTitle}",
                    ch.MihonProviderId, ch.Chapter.ChapterNumber, ch.Title);
                return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
            }
            string provider = src.Name + "(" + src.Language + ")";
            try
            {
                List<Page>? pages = await _mihon.MihonErrorWrapperAsync(
                                () => src.GetPagesAsync(ch.Chapter, token),
                                "Unable to get Pages from Chapter {Chapter}, Series {Title} from {provider}", ch.Chapter.ParsedNumber, ch.Title, provider).ConfigureAwait(false);
                if (pages==null)
                    return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
                ch.Pages = pages;
                if (ch.Pages.Count == 0)
                {
                    _logger.LogError("No pages found from source for provider {provider} when downloading chapter {ChapterNumber} of series {SeriesTitle}",
                        provider, ch.Chapter.ParsedNumber, ch.Title);
                    return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error getting pages from source for provider ID {ProviderId} when downloading chapter {ChapterNumber} of series {SeriesTitle}",
                    ch.MihonProviderId, ch.Chapter.ParsedNumber, ch.Title);
                return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
            }
            ch.PageCount = ch.Pages.Count;
            downloadSummary = ch.ToDownloadSummary();
            downloadSummary.PageCount = ch.PageCount;
            string providerName = ch.ProviderName;
            if (ch.Scanlator != null)
                providerName += "-" + ch.Scanlator;

            string chapterName = "";
            chapterName = $"chapter {ch.Chapter.ParsedNumber.FormatDecimal()} ";

            string? rchap = null;
            if (!string.IsNullOrEmpty(ch.ChapterName))
            {
                string cc = ch.ChapterName.Trim().ToLowerInvariant();
                if (!cc.Contains("ch.") && !cc.Contains("chapter"))
                    rchap = ch.ChapterName.Trim();
            }

            decimal? maxChap = null;
            SeriesProviderEntity? p = await _db.SeriesProviders.Where(a => a.Id == ch.SeriesProviderId).AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
            if (p != null)
                maxChap = p.Chapters.Max(c => c.Number);

            string zipFile = ArchiveHelperService.MakeFileNameSafe(ch.ProviderName, ch.Scanlator, ch.SeriesTitle, ch.Language, ch.Chapter.ParsedNumber, rchap, maxChap) + ".cbz";
            string message = $"Downloading ({providerName}) {ch.Title} {chapterName}...";
            reporter.Report(ProgressStatus.Started, 0, message, downloadSummary);

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
                    foreach(Page pag in ch.Pages)
                    {
                        try
                        {
                            page = pag.Index;
                            ContentTypeStream? image = await _mihon.MihonErrorWrapperAsync(
                                ()=>src.GetPageImageAsync(pag, token),
                                "Unable to get Page {Page} from Chapter {Chapter}, Series {Title} from {provider}", page+1, ch.Chapter.ParsedNumber, ch.Title, provider).ConfigureAwait(false);
                            if (image==null)
                            {
                                breaked = true;
                                break;
                            }

                            (_, string? ext) = image.GetImageMimeTypeAndExtension();
                            if (ext == null)
                            {
                                _logger.LogWarning("Page {Page} of chapter {ChapterNumber} of series {SeriesTitle} is not a valid image", page+1, ch.Chapter.ParsedNumber, ch.Title);
                                ext = ".unk";
                            }
                            string fileName = ArchiveHelperService.MakeFileNameSafe(ch.ProviderName, ch.Scanlator, ch.SeriesTitle, ch.Language,
                                        ch.Chapter.ParsedNumber, ch.ChapterName, maxChap, page + 1, ch.PageCount) + ext;
                            zipWriter.Write(fileName, image);
                            acum += step;
                            message = $"Downloading ({providerName}) {ch.Title} {chapterName} {page}";
                            reporter.Report(ProgressStatus.InProgress, (int)acum, message, downloadSummary);
                        }
                        catch (Exception)
                        {
                            _logger.LogError("Failed to download page {Page} for chapter {ChapterNumber} of series {SeriesTitle}",
                                page+1, ch.Chapter.ParsedNumber, ch.Title);
                            breaked = true;
                            break;
                        }

                        if (breaked)
                            break;
                    }

                    if (page == 0)
                    {
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
                    reporter.Report(ProgressStatus.Failed, (int)acum, message, downloadSummary);
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
                    reporter.Report(ProgressStatus.Failed, (int)acum, message, downloadSummary);
                    return await RescheduleDownloadAsync(ch, token).ConfigureAwait(false);
                }

                using (var n = await _lock.LockAsync(ch.SeriesId.ToString(), token).ConfigureAwait(false))
                {
                    SeriesProviderEntity? providerr = await _db.SeriesProviders.FirstOrDefaultAsync(a => a.Id == ch.SeriesProviderId, token).ConfigureAwait(false);
                    if (providerr == null)
                    {
                        _logger.LogWarning("Series Provider {ProviderName} no longer exists.", ch.ProviderName);
                        reporter.Report(ProgressStatus.Completed, 100, "", downloadSummary);
                        return JobResult.Failed;
                    }

                    Models.Chapter? cha = providerr.Chapters.FirstOrDefault(c => c.Number == ch.Chapter.ParsedNumber);
                    if (cha == null)
                    {
                        cha = new Models.Chapter();
                        providerr.Chapters.Add(cha);
                        providerr.Chapters = providerr.Chapters.OrderBy(c => c.Number).ToList();
                    }

                    cha.PageCount = page;
                    cha.IsDeleted = false;
                    cha.Name = ch.Chapter.Name;
                    cha.Number = ch.Chapter.ParsedNumber;
                    cha.DownloadDate = DateTime.UtcNow;
                    cha.ProviderUploadDate = ch.ComicUploadDateUTC;
                    cha.Filename = zipFile;
                    cha.ShouldDownload = false;
                    providerr.ContinueAfterChapter = providerr.Chapters.MaxNull(c => c.Number);
                    providerr.ChapterCount = providerr.Chapters.Count;
                    _db.Touch(providerr, a => a.Chapters);
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);

                    Models.Database.SeriesEntity s = await _db.Series.Include(a => a.Sources).Where(a => a.Id == providerr.SeriesId).FirstAsync(token);
                    if (providerr.IsStorage)
                    {
                        List<Models.Chapter> chapters = s.Sources.Where(a => !a.IsDisabled && !a.IsUninstalled && !a.IsStorage)
                            .SelectMany(a => a.Chapters).Where(c => c.Number == ch.Chapter.ParsedNumber && !string.IsNullOrEmpty(c.Filename)).ToList();
                        if (chapters.Count > 0)
                        {
                            //Delete temporary sources chapters if needed, since we have the storage one
                            foreach (Models.Chapter c in chapters)
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
                    await s.SaveImportSeriesSnapshotToDirectoryAsync(fullPath, _logger, token).ConfigureAwait(false);
                }

                message = $"Downloading ({providerName}) {ch.Title} {chapterName} completed.";
                reporter.Report(ProgressStatus.Completed, 100, message, downloadSummary);
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
                reporter.Report(ProgressStatus.Failed, (int)100, "Error downloading chapter", downloadSummary);
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
            EnqueueEntity? d = await _db.Queues.Where(a => a.Id == id && a.JobType == JobType.Download).AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
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
                EnqueueEntity delete = await _db.Queues.FirstAsync(a => a.Id == id, token).ConfigureAwait(false);
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
        public async Task<JobResult> QueueChapterDownloadsAsync(SeriesProviderEntity serie, List<ChapterDownload> chaps, CancellationToken token = default)
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

                foreach (ChapterDownload ch in chaps.OrderBy(a => a.Index))
                {
                    string key = $"{ch.MihonId}|{ch.Index}";
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
            SettingsDto appSettings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            download.Retries++;
            string key = $"{download.MihonId}|{download.Index}";

            if (download.Retries > appSettings.ChapterDownloadFailRetries)
            {
                _logger.LogWarning("Max retries reached for chapter {ChapterNumber} of series {SeriesTitle} from {ProviderName}. Giving up.", download.Chapter.ChapterNumber, download.Title, download.ProviderName);
                return JobResult.Failed;
            }
            
            string groupKey = $"{download.MihonId}";
            DateTime nextTime = DateTime.UtcNow.Add(appSettings.ChapterDownloadFailRetryTime);
            await _jobManagementService.ScheduleJobAsync(JobType.Download, download, nextTime, "Downloads", key, groupKey, download.SeriesId.ToString(), Priority.Normal, download.Retries, token).ConfigureAwait(false);
            return JobResult.Handled;
        }
    }
}
