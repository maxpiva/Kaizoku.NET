using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Settings;

namespace KaizokuBackend.Services.Images.Providers
{
    public class StorageImageProvider : IImageProvider
    {
        private readonly SettingsService _settingsService;
        public StorageImageProvider(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }
        public bool CanProcess(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            if (url.StartsWith("storage://"))
                return true;
            return false;
        }
        public Task<Stream?> ObtainStreamAsync(EtagCacheEntity cache, CancellationToken token)
        {
            string? storagePath = _settingsService.DirectSettings?.StorageFolder;
            if (string.IsNullOrEmpty(storagePath))
                return Task.FromResult((Stream?)null);
            string path = cache.Url.Substring(10);
            string finalPath = Path.GetFullPath(Path.Combine(storagePath, path));
            if (File.Exists(finalPath))
                return Task.FromResult((Stream?)File.OpenRead(finalPath));
            return Task.FromResult((Stream?)null);
        }
    }
}
