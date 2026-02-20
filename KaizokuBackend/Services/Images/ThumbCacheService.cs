using com.sun.org.apache.bcel.@internal.generic;
using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Images.Providers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace KaizokuBackend.Services.Images
{

    public class ThumbCacheService
    {
        
        private readonly ILogger _logger;
        private readonly IHttpClientFactory _factory;
        private readonly AppDbContext _db;
        private readonly CacheOptions _options;
        private readonly List<IImageProvider> _imageProviders;

        private readonly static Dictionary<string, string> _urlCache = new Dictionary<string, string>();
        private readonly static Dictionary<string, EtagCacheEntity> _etagCache = new Dictionary<string,EtagCacheEntity>();
        private readonly static SemaphoreSlim _urlLock = new SemaphoreSlim(1);
        private readonly static SemaphoreSlim _eTagLock = new SemaphoreSlim(1);

        public ThumbCacheService(IOptions<CacheOptions> options,
            ILogger<ThumbCacheService> logger,
            AppDbContext db,
            IHttpClientFactory factory,
            IWorkingFolderStructure workingFolderStructure,
            MihonBridgeService mihonBridgeService,
            IEnumerable<IImageProvider> imageProviders
            )
        {
            _db = db;
            _logger = logger;          
            _factory = factory;
            _options = options.Value;
            _imageProviders = imageProviders.ToList();
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
                if (string.IsNullOrEmpty(url))
                    return string.Empty;
                if (!_urlCache.ContainsKey(url))
                {
                    EtagCacheEntity? c = await _db.ETagCache.AsNoTracking().FirstOrDefaultAsync(e => e.Url == url, token).ConfigureAwait(false);
                    if (c == null)
                        c = await AddInternalUrlAsync(url, null, token).ConfigureAwait(false);
                    if (c == null)
                        return string.Empty;
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
                Dictionary<string, List<IThumb>> allUrl = all.GroupBy(a => a.ThumbnailUrl, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                etags = await _db.ETagCache.AsNoTracking().Where(e => allUrl.Keys.Contains(e.Url)).ToListAsync(token).ConfigureAwait(false);                
                foreach(EtagCacheEntity m in etags)
                {
                    List<IThumb> allT = allUrl[m.Url];
                    foreach (IThumb t in allT)
                    {
                        _urlCache[t.ThumbnailUrl] = m!.Key;
                        t.ThumbnailUrl = prefix + m!.Key;
                        all.Remove(t);
                    }
                }

                foreach (IThumb t in all)
                {
                    EtagCacheEntity? ee = await AddInternalUrlAsync(t.ThumbnailUrl, null, token).ConfigureAwait(false);
                    if (ee == null)
                        continue;
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
        
        public async Task<string?> AddUrlAsync(string url, string? mihonProviderId, CancellationToken token = default)
        {
            EtagCacheEntity? cac = await AddInternalUrlAsync(url, mihonProviderId, token).ConfigureAwait(false);
            return cac?.Url;
        }
     
        private async Task<EtagCacheEntity?> AddInternalUrlAsync(string url, string? mihonProviderId, CancellationToken token = default)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    _logger.LogWarning("Url is null or empty.");
                    return null;
                }
                IImageProvider? provider = GetProviderForUrl(url);
                if (provider == null)
                    return null;
                string key = Guid.NewGuid().ToString("N");
                var existingEntry = await _db.ETagCache.FirstOrDefaultAsync(e => e.Url == url, token).ConfigureAwait(false);
                if (existingEntry != null)
                    return existingEntry;
                var newCacheEntry = new Models.Database.EtagCacheEntity
                {
                    Key = key,
                    Url = url,
                    MihonProviderId = mihonProviderId,
                    NextUpdateUTC = DateTime.UtcNow.Add(GetCacheDuration())
                };
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
        IImageProvider? GetProviderForUrl(string url)
        {
            foreach(IImageProvider p in _imageProviders)
            {
                if (p.CanProcess(url))
                {
                    return p;
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
                IImageProvider? provider = GetProviderForUrl(cache.Url);
                if (provider is UrlImageProvider urlImageProvider)
                    await urlImageProvider.UpdateCacheWithRemoteAsync(cache, httpClient, token).ConfigureAwait(false);
                else
                {
                    cache.NextUpdateUTC = DateTime.UtcNow.Add(TimeSpan.FromDays(_options.AgeInDays > 0 ? _options.AgeInDays : 1));
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                }
            }
        }
       
        public TimeSpan GetCacheDuration()
        {
            return TimeSpan.FromDays(_options.AgeInDays > 0 ? _options.AgeInDays : 1);
        }
       
        public async Task<Stream?> GetStreamAsync(EtagCacheEntity entry, CancellationToken token = default)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Url))
                return null;
            IImageProvider? img = GetProviderForUrl(entry.Url);
            if (img == null)
                return null;
            return await img.ObtainStreamAsync(entry, token).ConfigureAwait(false);
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
            IImageProvider? img = GetProviderForUrl(cacheEntry!.Url);
            if (img==null)
                return (HttpStatusCode.NotFound, null, null, null);
            Stream? s = await img.ObtainStreamAsync(cacheEntry!, token).ConfigureAwait(false);
            if (s==null)
                return (HttpStatusCode.NotFound, null, null, null);
            string contentType = cacheEntry!.ContentType;
            if (string.IsNullOrEmpty(contentType))
            {
                (string? detectedContentType, string? detectedExtension) = s.GetImageMimeTypeAndExtension();
                s.Position = 0;
                contentType = detectedContentType ?? "";
            }
            return (HttpStatusCode.OK, cacheEntry!.Etag, contentType, s);
        }
    }
}
