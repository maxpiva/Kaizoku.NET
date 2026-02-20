using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Settings;

namespace KaizokuBackend.Services.Images.Providers
{
    public class StorageImageProvider : IImageProvider
    {
        private readonly SettingsService _settingsService;
        private readonly AppDbContext _db;
        public StorageImageProvider(AppDbContext db, SettingsService settingsService)
        {
            _settingsService = settingsService;
            _db = db;
        }
        public bool CanProcess(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            if (url.StartsWith("storage://"))
                return true;
            return false;
        }
        public async Task<Stream?> ObtainStreamAsync(EtagCacheEntity cache, CancellationToken token)
        {
            string? storagePath = _settingsService.DirectSettings?.StorageFolder;
            if (string.IsNullOrEmpty(storagePath))
                return null;
            string path = cache.Url.Substring(10);
            string finalPath = Path.GetFullPath(Path.Combine(storagePath, path));
            if (File.Exists(finalPath))
            {
                Stream stream = File.OpenRead(finalPath);
                if (string.IsNullOrEmpty(cache.Etag))
                {
                    cache.Etag = await UrlImageProvider.ComputeMd5HashFromStreamAsync(stream);
                    stream.Position = 0;
                    await _db.SaveChangesAsync();
                }
                return stream;
            }
            return null;
        }
    }
}
