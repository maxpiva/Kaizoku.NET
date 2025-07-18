using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace KaizokuBackend.Services.Series
{
    /// <summary>
    /// Service responsible for querying series data
    /// </summary>
    public class SeriesQueryService
    {
        private readonly AppDbContext _db;
        private readonly ContextProvider _baseUrl;
        private readonly SettingsService _settings;
        private readonly EtagCacheService _etagCacheService;
        private readonly SuwayomiClient _suwayomi;

        private readonly ILogger<SeriesQueryService> _logger;

        public SeriesQueryService(AppDbContext db, SuwayomiClient suwayomi, EtagCacheService etagCacheService, ContextProvider baseUrl, SettingsService settings, ILogger<SeriesQueryService> logger)
        {
            _db = db;
            _baseUrl = baseUrl;
            _settings = settings;
            _etagCacheService = etagCacheService;
            _suwayomi = suwayomi;
            _logger = logger;
        }

        /// <summary>
        /// Gets detailed information about a series by its unique identifier
        /// </summary>
        /// <param name="uid">The unique identifier of the series</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Extended information about the series</returns>
        public async Task<SeriesExtendedInfo> GetSeriesAsync(Guid uid, CancellationToken token = default)
        {
            Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            Models.Database.Series? s = await _db.Series
                .Include(a => a.Sources)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == uid, token);
            if (s == null)
                return new SeriesExtendedInfo();
            return s.ToSeriesExtendedInfo(_baseUrl, settings);
        }
        /// <summary>
        /// Gets the thumbnail for a series (moved from SeriesResourceService)
        /// </summary>
        public async Task<IActionResult> GetSeriesThumbnailAsync(string id, CancellationToken token = default)
        {
            int fid = 0;
            if (id != "unknown")
            {
                string[] split = id.Split('!');
                string realId = id;
                if (split.Length > 1)
                    realId = split[0];
                fid = int.Parse(realId);
            }

            var ret = await _etagCacheService.ETagWrapperAsync(id, async () =>
            {
                if (id.StartsWith("unknown"))
                    return FileSystemExtensions.StreamEmbeddedResource("na.jpg") ?? new MemoryStream();
                else
                    return await _suwayomi.GetMangaThumbnailAsync(fid, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            if (ret is StatusCodeResult r)
            {
                if (r.StatusCode == (int)HttpStatusCode.NotFound)
                {
                    return new FileStreamResult(
                        FileSystemExtensions.StreamEmbeddedResource("na.jpg") ?? new MemoryStream(), "image/jpeg");
                }
            }

            return ret;
        }
        /// <summary>
        /// Gets the user's library of series
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of series in the library</returns>
        public async Task<List<SeriesInfo>> GetLibraryAsync(CancellationToken token = default)
        {
            List<Models.Database.Series> series = await _db.Series
                .Include(s => s.Sources).AsNoTracking().ToListAsync(token);
            return series.Select(a => a.ToSeriesInfo(_baseUrl)).ToList();
        }

        /// <summary>
        /// Gets the latest series with optional filtering
        /// </summary>
        /// <param name="start">Starting index for pagination</param>
        /// <param name="count">Number of items to return</param>
        /// <param name="sourceid">Optional source ID filter</param>
        /// <param name="keyword">Optional keyword filter</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of latest series information</returns>
        public async Task<List<LatestSeriesInfo>> GetLatestAsync(int start, int count, string? sourceid = null,
            string? keyword = null, CancellationToken token = default)
        {
            IQueryable<LatestSerie> series = _db.LatestSeries;
            if (!string.IsNullOrEmpty(sourceid))
            {
                series = series.Where(a => a.SuwayomiSourceId == sourceid);
            }

            if (!string.IsNullOrEmpty(keyword))
                series = series.Where(a => EF.Functions.Like(a.Title, $"%{keyword}%"));

            series = series.OrderByDescending(a => a.FetchDate);
            if (start > 0)
                series = series.Skip(start);

            return (await series.Take(count).ToListAsync(token).ConfigureAwait(false))
                .Select(a => a.ToSeriesInfo(_baseUrl)).ToList();
        }
    }
}