using com.sun.org.apache.bcel.@internal.generic;
using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace KaizokuBackend.Services.Helpers
{
    public class CacheOptions
    {
        public string CachePath { get; set; }
        public int AgeInDays { get; set; }

    }

    public class ThumbCacheService
    {
        
        private readonly AppDbContext _db;
        private readonly ILogger _logger;
        private readonly IHttpClientFactory _factory;
        private readonly CacheOptions _options;
        private readonly IWorkingFolderStructure _workingFolderStructure;
        private readonly Dictionary<string, string> _urlCache = new Dictionary<string, string>();
        private readonly Dictionary<string, EtagCacheEntity> _etagCache = new Dictionary<string,EtagCacheEntity>();
        private readonly SemaphoreSlim _urlLock = new SemaphoreSlim(1);
        private readonly SemaphoreSlim _eTagLock = new SemaphoreSlim(1);


        public ThumbCacheService(IOptions<CacheOptions> options,
            ILogger<ThumbCacheService> logger,
            AppDbContext db,
            IHttpClientFactory factory,
            IWorkingFolderStructure workingFolderStructure
            )
        {
            _db = db;
            _logger = logger;
          
            _factory = factory;
            _options = options.Value;
            _workingFolderStructure = workingFolderStructure;
        }
        public TimeSpan GetCacheDuration()
        {
            return TimeSpan.FromDays(_options.AgeInDays > 0 ? _options.AgeInDays : 1);
        }
        public async ValueTask<EtagCacheEntity?> GetEtagAsync(string key, CancellationToken token = default)
        {
            await _eTagLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!_etagCache.ContainsKey(key))
                {
                    EtagCacheEntity? c = await _db.ETagCache.AsNoTracking().FirstOrDefaultAsync(e => e.Key == key, token).ConfigureAwait(false);
                    if (c == null)
                    {
                        _logger.LogWarning("ETag with key {key} not found in cache.", key);
                        return null;
                    }
                    _etagCache[key] = c;
                }
                return _etagCache[key];
            }
            finally
            {
                _eTagLock.Release();
            }
        }
        public async ValueTask<string> GetKeyAsync(string url, CancellationToken token = default)
        {
            await _urlLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!_urlCache.ContainsKey(url))
                {
                    EtagCacheEntity? c = await _db.ETagCache.AsNoTracking().FirstOrDefaultAsync(e => e.Url == url, token).ConfigureAwait(false);
                    if (c == null)
                    {
                        c = await AddInternalUrlAsync(url, token).ConfigureAwait(false);
                    }
                    _urlCache[url] = c!.Key;
                }
                return _urlCache[url];
            }
            finally
            {
                _urlLock.Release();
            }
        }
        public async ValueTask PopulateThumbsAsync(IThumb thumb, string prefix = "/api/image/", CancellationToken token = default)
        {
            thumb.ThumbnailUrl = prefix + await GetKeyAsync(thumb.ThumbnailUrl, token).ConfigureAwait(false);
        }
        public async ValueTask PopulateThumbsAsync(IEnumerable<IThumb> thumbs, string prefix = "/api/image/", CancellationToken token = default)
        {
            List<EtagCacheEntity> etags = [];
            await _urlLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                List<IThumb> all = thumbs.ToList();
                foreach(IThumb t in thumbs.ToList())
                {
                    string? url = t?.ThumbnailUrl;
                    if (t==null || string.IsNullOrEmpty(url))
                    {
                        all.Remove(t);
                        continue;
                    }
                    if (_urlCache.TryGetValue(url, out string k))
                    {
                        t.ThumbnailUrl = prefix + k;
                        all.Remove(t);
                    }
                }
                Dictionary<string, IThumb> allUrl = all.ToDictionary(a => a.ThumbnailUrl, a => a, StringComparer.OrdinalIgnoreCase);
                etags = await _db.ETagCache.AsNoTracking().Where(e => allUrl.Keys.Contains(e.Url)).ToListAsync(token).ConfigureAwait(false);                
                foreach(EtagCacheEntity m in etags)
                {
                    IThumb t = allUrl[m.Url];
                    _urlCache[t.ThumbnailUrl] = m!.Key;
                    t.ThumbnailUrl = prefix + m!.Key;
                    all.Remove(t);
                }

                foreach (IThumb t in all)
                {
                    EtagCacheEntity? ee = await AddInternalUrlAsync(t.ThumbnailUrl, token).ConfigureAwait(false);
                    _urlCache[t.ThumbnailUrl] = ee!.Key;
                    t.ThumbnailUrl = prefix + ee!.Key;
                    etags.Add(ee!);
                }
            }
            finally
            {
                _urlLock.Release();
            }
            if (etags.Count > 0)
            {
                await _eTagLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    foreach (EtagCacheEntity eee in etags)
                    {
                        if (!_etagCache.ContainsKey(eee.Key))
                            _etagCache[eee.Key] = eee;
                    }
                }
                finally
                {
                    _eTagLock.Release();
                }
            }
        }


        public async Task<bool> CheckETagAsync(string key, string? etag, CancellationToken token = default)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(etag))
                {
                    return false;
                }

                var cacheEntry = await _db.ETagCache.FirstOrDefaultAsync(e => e.Key == key, token).ConfigureAwait(false);

                if (cacheEntry == null)
                {
                    return false;
                }

                return cacheEntry.Etag == etag;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking ETag for key {key}: {ex.Message}");
                return false;
            }
        }


        public async Task<string?> CacheFromUrlAsync(string url, CancellationToken token = default)
        {
            var existingEntry = await _db.ETagCache.FirstOrDefaultAsync(e => e.Url == url, token).ConfigureAwait(false);
            if (existingEntry == null)
                return null;

            {                
                _logger.LogInformation($"Cache entry for the Url {url} already exists.");
                return existingEntry.Url;
            }
        }
        public async Task<string?> AddExtensionAsync(string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogWarning("path is null or empty.");
                return null;
            }
            string url = "ext://" + path;
            return await AddUrlAsync(url, token).ConfigureAwait(false);
        }
        public async Task<string?> AddUrlAsync(string url, CancellationToken token = default)
        {
            EtagCacheEntity? cac = await AddInternalUrlAsync(url, token).ConfigureAwait(false);
            return cac?.Url;
        }
     
        private async Task<EtagCacheEntity?> AddInternalUrlAsync(string url, CancellationToken token = default)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    _logger.LogWarning("Url is null or empty.");
                    return null;
                }
                string key = Guid.NewGuid().ToString("N");
                var existingEntry = await _db.ETagCache.FirstOrDefaultAsync(e => e.Url == url, token).ConfigureAwait(false);
                if (existingEntry != null)
                    return existingEntry;


                var newCacheEntry = new Models.Database.EtagCacheEntity
                {
                    Key = key,
                    Url = url,
                    NextUpdateUTC = DateTime.UtcNow
                };
                if (key.StartsWith("ext://"))
                {
                    string filename = key.Substring(6);
                    newCacheEntry.Extension = "."+Path.GetExtension(filename);
                }
                await _db.ETagCache.AddAsync(newCacheEntry, token).ConfigureAwait(false);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                return newCacheEntry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding cache entry for {url}: {ex.Message}");
                return null;
            }
        }

        public static async Task<string> ComputeMd5HashFromStreamAsync(Stream stream, CancellationToken token = default)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = await md5.ComputeHashAsync(stream, token).ConfigureAwait(false);
                return Convert.ToBase64String(hash);
            }
        }
        public async Task<Stream?> GetStreamAsync(Models.Database.EtagCacheEntity cache, CancellationToken token = default)
        {
            if (cache.Key.StartsWith("ext://"))
            {
                string originalFilename = cache.Key.Substring(6);
                string finalPath = Path.GetFullPath(Path.Combine(_workingFolderStructure.WorkingFolder, originalFilename));
                if (File.Exists(finalPath))
                    return File.OpenRead(finalPath);
                return null;
            }
            string directory = Path.Combine(_options.CachePath, cache.Key.Substring(0, 2));
            if (!string.IsNullOrEmpty(cache.Extension))
            {
                string baseFile = Path.Combine(directory, cache.Key.Substring(2)) + cache.Extension;
                if (File.Exists(baseFile))
                {
                    return File.OpenRead(baseFile);
                }
            }
            var httpClient = _factory.CreateClient(nameof(ThumbCacheService));
            await UpdateCacheWithRemoteAsync(cache, httpClient, token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(cache.Extension))
            {
                string baseFile = Path.Combine(directory, cache.Key.Substring(2)) + cache.Extension;
                if (File.Exists(baseFile))
                {
                    return File.OpenRead(baseFile);
                }
            }
            return null;
        }
        public async Task UpdateAllCacheWithRemoteAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            List<EtagCacheEntity> caches = await _db.ETagCache.Where(a=>a.NextUpdateUTC<now).ToListAsync(token).ConfigureAwait(false);
            var httpClient = _factory.CreateClient(nameof(ThumbCacheService));
            foreach (EtagCacheEntity cache in caches)
            {
                await UpdateCacheWithRemoteAsync(cache, httpClient, token).ConfigureAwait(false);
            }
        }

        public async Task UpdateCacheWithRemoteAsync(EtagCacheEntity cache, HttpClient httpClient, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(cache.Url))
            {
                _logger.LogWarning($"Cache URL is null or empty for {cache.Key}");
                return;
            }
            try
            {
                string directory = Path.Combine(_options.CachePath, cache.Key.Substring(0, 2));
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                string baseFile = Path.Combine(directory, cache.Key.Substring(2));
                string originalFile = null;
                if (!string.IsNullOrEmpty(cache.Extension))
                {
                    originalFile = baseFile + cache.Extension;
                    if (!File.Exists(originalFile))
                        cache.ExternalEtag = string.Empty;
                }
                using var memoryStream = new MemoryStream();
                string mediaType = "application/octet-stream";
                using var request = new HttpRequestMessage(HttpMethod.Get, cache.Url);
                if (!string.IsNullOrWhiteSpace(cache.ExternalEtag))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", cache.ExternalEtag);
                }
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                if (response.Headers.ETag != null)
                {
                    cache.ExternalEtag = response.Headers.ETag.Tag;
                }

                TimeSpan cacheDuration = ResolveCacheDuration(response);

                if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotModified)
                {
                    cache.NextUpdateUTC = DateTime.UtcNow.Add(cacheDuration);
                    await _db.SaveChangesAsync().ConfigureAwait(false);
                    return;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.LogWarning("Unexpected status code {StatusCode} while refreshing cache for {Key}", response.StatusCode, cache.Key);
                    return;
                }
                await response.Content.CopyToAsync(memoryStream, token).ConfigureAwait(false);
                string? med = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrEmpty(med))
                    mediaType = med;
                if (memoryStream.Length == 0)
                {
                    _logger.LogWarning("Received empty payload when refreshing cache for {Key}", cache.Key);
                    return;
                }
                memoryStream.Position = 0;
                cache.Etag = await ComputeMd5HashFromStreamAsync(memoryStream, token).ConfigureAwait(false);
                memoryStream.Position = 0;
                (string? detectedContentType, string? detectedExtension) = memoryStream.GetImageMimeTypeAndExtension();
                var contentType = !string.IsNullOrWhiteSpace(detectedContentType)
                    ? detectedContentType
                    : mediaType;

                var normalizedExtension = NormalizeExtension(detectedExtension);
                if (string.IsNullOrEmpty(normalizedExtension))
                {
                    normalizedExtension = NormalizeExtension(Path.GetExtension(cache.Url));
                    if (string.IsNullOrEmpty(normalizedExtension))
                    {
                        normalizedExtension = ".bin";
                    }
                }
                var targetFile = baseFile + normalizedExtension;
                if (originalFile != null && File.Exists(originalFile) && (originalFile != baseFile))
                {
                    try
                    {
                        File.Delete(originalFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete old cache file for {Key}", cache.Key);
                    }
                }

                memoryStream.Position = 0;
                using (var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true))
                {
                    await memoryStream.CopyToAsync(fileStream, token).ConfigureAwait(false);
                }
                cache.Extension = normalizedExtension;
                cache.ContentType = contentType;
                cache.NextUpdateUTC = DateTime.UtcNow.Add(cacheDuration);
                await _db.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cache for key {Key}", cache.Key);
            }
        }
        TimeSpan ResolveCacheDuration(HttpResponseMessage responseMessage)
        {
            var cacheControl = responseMessage.Headers.CacheControl;
            var maxAge = cacheControl?.MaxAge ?? cacheControl?.SharedMaxAge ?? cacheControl?.MaxStaleLimit;

            if (maxAge.HasValue && maxAge.Value > TimeSpan.Zero)
            {
                return maxAge.Value;
            }

            var fallbackDays = _options.AgeInDays > 0 ? _options.AgeInDays : 1;
            return TimeSpan.FromDays(fallbackDays);
        }

        static string NormalizeExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return string.Empty;            }

            var trimmed = extension.Trim();
            return trimmed.StartsWith('.') ? trimmed : "." + trimmed.TrimStart('.');
        }

        public async Task<(HttpStatusCode StatusCode, string? etag, string? mimetype, Stream? stream)> ProcessKeyAsync(string key, string etag, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(key))
                return (HttpStatusCode.BadRequest, null, null, null);
            var cacheEntry = await _db.ETagCache.FirstOrDefaultAsync(e => e.Key == key, token).ConfigureAwait(false);
            if (cacheEntry == null)
                return (HttpStatusCode.NotFound, null, null, null);
            if (cacheEntry != null && !string.IsNullOrEmpty(etag) && etag.Equals(cacheEntry.Etag, StringComparison.OrdinalIgnoreCase))
                return (HttpStatusCode.NotModified, null, null, null);
            Stream? s = await GetStreamAsync(cacheEntry!, token).ConfigureAwait(false);
            if (s==null)
                return (HttpStatusCode.NotFound, null, null, null);
            return (HttpStatusCode.OK, cacheEntry!.Etag, cacheEntry!.ContentType, s);
        }
    }
}
