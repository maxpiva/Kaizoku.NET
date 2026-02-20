using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Services.Images;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Helpers
{
    public class NouisanceFixer20ExtraLarge
    {

        private readonly AppDbContext _db;
        private readonly SettingsService _settingsService;
        private readonly ThumbCacheService _thumbs;

        public NouisanceFixer20ExtraLarge(AppDbContext db, SettingsService settingsService, ThumbCacheService thumbs)
        {
            _db = db;
            _settingsService = settingsService;
            _thumbs = thumbs;
        }



        public async Task FixThumbnailsOfSeriesWithMissingThumbnailsAsync(CancellationToken token)
        {
            //Some series no longer have providers, or never had, try to fix the images of those, if we have a local file to use as thumbnail
            SettingsDto settings = await _settingsService.GetSettingsAsync(token).ConfigureAwait(false);
            string storageFolder = settings.StorageFolder ?? string.Empty;
            bool storageAvailable = !string.IsNullOrWhiteSpace(storageFolder) && Directory.Exists(storageFolder);

            List<SeriesEntity> badThumbSeries = await _db.Series.Where(a => a.ThumbnailUrl.StartsWith("serie/thumb/")).ToListAsync(token).ConfigureAwait(false);
            foreach(SeriesEntity series in badThumbSeries)
            {
                series.ThumbnailUrl = string.Empty;
            }
            List<SeriesProviderEntity> badThumbSeriesProviders = await _db.SeriesProviders.Where(a => a.ThumbnailUrl!=null && a.ThumbnailUrl.StartsWith("serie/thumb/")).ToListAsync(token).ConfigureAwait(false);
            foreach(SeriesProviderEntity provider in badThumbSeriesProviders)
            {
                provider.ThumbnailUrl = string.Empty;
            }
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            List<SeriesEntity> seriesMissingThumb = await _db.Series.Where(a => string.IsNullOrEmpty(a.ThumbnailUrl)).ToListAsync(token).ConfigureAwait(false);
            List<Guid> ids = seriesMissingThumb.Select(a => a.Id).ToList();
            List<Guid> providerSeriesIds = await _db.SeriesProviders
                .Where(a => string.IsNullOrEmpty(a.ThumbnailUrl) && (a.IsLocal || a.IsUnknown))
                .Select(a => a.SeriesId)
                .Distinct()
                .ToListAsync(token)
                .ConfigureAwait(false);
            ids.AddRange(providerSeriesIds.Where(a => !ids.Contains(a)));
            if (ids.Count == 0)
            {
                return;
            }

            List<SeriesEntity> seriesList = await _db.Series.Where(a => ids.Contains(a.Id)).ToListAsync(token).ConfigureAwait(false);
            List<SeriesProviderEntity> providers = await _db.SeriesProviders.Where(a => ids.Contains(a.SeriesId)).ToListAsync(token).ConfigureAwait(false);
            Dictionary<Guid, List<SeriesProviderEntity>> providersBySeries = providers
                .GroupBy(a => a.SeriesId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (SeriesEntity series in seriesList)
            {
                if (!providersBySeries.TryGetValue(series.Id, out List<SeriesProviderEntity>? providerList))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(series.ThumbnailUrl))
                {
                    TryAssignFromProviders(series, providerList);
                }

                if (storageAvailable && !string.IsNullOrEmpty(series.StoragePath))
                {
                    await TryAssignFromStorageAsync(series, providerList, storageFolder, token).ConfigureAwait(false);
                }
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        private static bool TryAssignFromProviders(SeriesEntity series, List<SeriesProviderEntity> providers)
        {
            SeriesProviderEntity? candidate = providers
                .Where(p => !string.IsNullOrEmpty(p.ThumbnailUrl))
                .OrderByDescending(p => p.IsCover)
                .FirstOrDefault();

            if (candidate == null)
            {
                return false;
            }

            series.ThumbnailUrl = candidate.ThumbnailUrl;
            EnsureSingleCover(candidate, providers);
            return true;
        }

        private async Task TryAssignFromStorageAsync(SeriesEntity series, List<SeriesProviderEntity> providers, string storageFolder, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(series.StoragePath))
            {
                return;
            }

            SeriesProviderEntity? localProvider = providers
                .Where(p => string.IsNullOrEmpty(p.ThumbnailUrl))
                .OrderByDescending(p => p.IsCover)
                .ThenByDescending(p => p.IsLocal || p.IsUnknown)
                .FirstOrDefault();

            if (localProvider == null)
            {
                return;
            }

            string imagePart = Path.Combine(series.StoragePath, "cover.jpg");
            string imageFullPath = Path.Combine(storageFolder, imagePart);
            if (!File.Exists(imageFullPath))
            {
                return;
            }

            localProvider.ThumbnailUrl = $"storage://{imagePart}";
            await _thumbs.AddStorageImageAsync(imagePart, token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(series.ThumbnailUrl))
            {
                series.ThumbnailUrl = localProvider.ThumbnailUrl;
                EnsureSingleCover(localProvider, providers);
            }
            else if (!providers.Any(p => p.IsCover))
            {
                EnsureSingleCover(localProvider, providers);
            }
        }

        private static void EnsureSingleCover(SeriesProviderEntity coverProvider, IEnumerable<SeriesProviderEntity> providers)
        {
            foreach (SeriesProviderEntity provider in providers)
            {
                provider.IsCover = provider.Id == coverProvider.Id;
            }
        }
    }
}
