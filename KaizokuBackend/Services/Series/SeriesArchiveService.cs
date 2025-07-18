using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Jobs.Report;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Series
{
    /// <summary>
    /// Service responsible for archive operations and series integrity checks
    /// </summary>
    public class SeriesArchiveService
    {
        private readonly AppDbContext _db;
        private readonly SettingsService _settings;
        private readonly ArchiveHelperService _archiveHelper;
        private readonly JobHubReportService _reportingService;
        private readonly ILogger<SeriesArchiveService> _logger;

        public SeriesArchiveService(AppDbContext db, SettingsService settings, ArchiveHelperService archiveHelper,
            JobHubReportService reportingService, ILogger<SeriesArchiveService> logger)
        {
            _db = db;
            _settings = settings;
            _archiveHelper = archiveHelper;
            _reportingService = reportingService;
            _logger = logger;
        }

        /// <summary>
        /// Verifies the integrity of series archive files
        /// </summary>
        /// <param name="seriesId">The series ID to verify</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Series integrity result</returns>
        public async Task<SeriesIntegrityResult> VerifyIntegrityAsync(Guid seriesId, CancellationToken token = default)
        {
            KaizokuBackend.Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            Models.Database.Series? series = await _db.Series.Include(a => a.Sources).Where(a => a.Id == seriesId)
                .FirstOrDefaultAsync(token).ConfigureAwait(false);
            
            if (series == null)
                throw new ArgumentException("Invalid series Id");
            
            string basePath = Path.Combine(settings.StorageFolder, series.StoragePath);
            
            // Remove empty unknown providers
            SeriesProvider? sp = series.Sources.FirstOrDefault(a =>
                a.IsUnknown && a.Chapters.All(a => string.IsNullOrEmpty(a.Filename)));
            if (sp != null)
            {
                _db.SeriesProviders.Remove(sp);
                series.Sources.Remove(sp);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }

            List<Chapter> chaps = series.Sources.SelectMany(a => a.Chapters)
                .Where(a => !string.IsNullOrEmpty(a.Filename)).ToList();
            
            return GetIntegrityResult(basePath, chaps);
        }

        /// <summary>
        /// Cleans up corrupted series files and marks chapters for re-download
        /// </summary>
        /// <param name="seriesId">The series ID to cleanup</param>
        /// <param name="token">Cancellation token</param>
        public async Task CleanupSeriesAsync(Guid seriesId, CancellationToken token = default)
        {
            KaizokuBackend.Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            Models.Database.Series? series = await _db.Series.Include(a => a.Sources).Where(a => a.Id == seriesId)
                .FirstOrDefaultAsync(token).ConfigureAwait(false);
            
            if (series == null)
                throw new ArgumentException("Invalid series Id");
            
            List<Chapter> chaps = series.Sources.SelectMany(a => a.Chapters)
                .Where(a => !string.IsNullOrEmpty(a.Filename)).ToList();
            
            SeriesIntegrityResult sr = GetIntegrityResult(series.StoragePath, chaps);
            bool update = false;
            string basePath = Path.Combine(settings.StorageFolder, series.StoragePath);

            foreach (ArchiveIntegrityResult r in sr.BadFiles)
            {
                if (r.Result == ArchiveResult.Fine)
                    continue;
                
                if (r.Result == ArchiveResult.NoImages || r.Result == ArchiveResult.NotAnArchive)
                {
                    string finalName = Path.Combine(basePath, r.Filename);
                    try
                    {
                        File.Delete(finalName);
                    }
                    catch (Exception)
                    {
                        _logger.LogWarning("Unable to delete file {finalName}", finalName);
                    }
                }

                // Mark chapters for re-download
                foreach (SeriesProvider s in series.Sources)
                {
                    foreach (Chapter ch in s.Chapters.Where(a => a.Filename == r.Filename))
                    {
                        ch.Filename = null;
                        ch.IsDeleted = true;
                        _db.Touch(s, c => c.Chapters);
                        update = true;
                        if (s.ContinueAfterChapter >= ch.Number)
                            s.ContinueAfterChapter = ch.Number - 1;
                    }
                }
            }

            if (update)
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates all series titles and comic info files
        /// </summary>
        /// <param name="jobInfo">Job information for progress reporting</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Job result</returns>
        public async Task<JobResult> UpdateAllSeriesAsync(JobInfo jobInfo, CancellationToken token = default)
        {
            ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
            await _archiveHelper.UpdateAllTitlesAndAddComicInfoAsync(progress, false, token).ConfigureAwait(false);
            return JobResult.Success;
        }

        /// <summary>
        /// Checks archive integrity and returns result
        /// </summary>
        /// <param name="path">Base path for the series</param>
        /// <param name="chapters">List of chapters to check</param>
        /// <returns>Series integrity result</returns>
        private static SeriesIntegrityResult GetIntegrityResult(string path, List<Chapter> chapters)
        {
            SeriesIntegrityResult result = new SeriesIntegrityResult
            {
                BadFiles = []
            };

            foreach (Chapter c in chapters)
            {
                string fileName = Path.Combine(path, c.Filename!);
                ArchiveResult ar = ArchiveHelperService.CheckArchive(fileName);
                if (ar != ArchiveResult.Fine)
                {
                    result.BadFiles.Add(new ArchiveIntegrityResult 
                    { 
                        Filename = c.Filename!, 
                        Result = ar 
                    });
                }
            }

            result.Success = result.BadFiles.Count == 0;
            return result;
        }
    }
}