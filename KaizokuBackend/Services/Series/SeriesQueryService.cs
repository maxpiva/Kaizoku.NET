using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Core.Extensions;
using System.Net;

namespace KaizokuBackend.Services.Series
{
    /// <summary>
    /// Service responsible for querying series data
    /// </summary>
    public class SeriesQueryService
    {
        private readonly AppDbContext _db;
        private readonly SettingsService _settings;
        private readonly ProviderCacheService _providerCache;
        private readonly ILogger<SeriesQueryService> _logger;

        public SeriesQueryService(AppDbContext db, SettingsService settings, ProviderCacheService providerCache, ILogger<SeriesQueryService> logger)
        {
            _db = db;          
            _settings = settings;
            _providerCache = providerCache;
            _logger = logger;
        }
        
        /// <summary>
        /// Gets detailed information about a series by its unique identifier
        /// </summary>
        /// <param name="uid">The unique identifier of the series</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Extended information about the series</returns>
        public async Task<SeriesExtendedDto> GetSeriesAsync(Guid uid, CancellationToken token = default)
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            Models.Database.SeriesEntity? s = await _db.Series
                .Include(a => a.Sources)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == uid, token);
            if (s == null)
                return new SeriesExtendedDto();
            return s.ToSeriesExtendedInfo(settings);
        }
        /*
        /// <summary>
        /// Gets the thumbnail for a series (moved from SeriesResourceService)
        /// </summary>
        public async Task<IActionResult> GetSeriesThumbnailAsync(string id, CancellationToken token = default)
        {
            var ret = await _etagCacheService.ETagWrapperAsync(id, async () =>
            {
                return await _thumbnailService.GetThumbnailAsync(id, token).ConfigureAwait(false);
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
        */
        /// <summary>
        /// Gets the user's library of series
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of series in the library</returns>
        public async Task<List<SeriesInfoDto>> GetLibraryAsync(CancellationToken token = default)
        {
            List<Models.Database.SeriesEntity> series = await _db.Series
                .Include(s => s.Sources).AsNoTracking().ToListAsync(token);
            return series.Select(a => a.ToSeriesInfo()).ToList();
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
        public async Task<List<LatestSeriesDto>> GetLatestAsync(int start, int count, string? mihonProviderId = null,
            string? keyword = null, CancellationToken token = default)
        {
            IQueryable<LatestSerieEntity> series = _db.LatestSeries;
            if (!string.IsNullOrEmpty(mihonProviderId))
            {
                series = series.Where(a => a.MihonProviderId == mihonProviderId);
            }

            if (!string.IsNullOrEmpty(keyword))
                series = series.Where(a => EF.Functions.Like(a.Title, $"%{keyword}%"));

            series = series.OrderByDescending(a => a.FetchDate);
            if (start > 0)
                series = series.Skip(start);

            return (await series.Take(count).ToListAsync(token).ConfigureAwait(false))
                .Select(a => a.ToSeriesInfo()).ToList();
        }
    }
}