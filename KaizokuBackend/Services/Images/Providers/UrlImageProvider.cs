using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Bridge;
using Microsoft.Extensions.Options;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.Net;
using System.Security.Cryptography;

namespace KaizokuBackend.Services.Images.Providers
{
    public class UrlImageProvider : IImageProvider
    {
        private readonly ILogger _logger;
        private readonly IHttpClientFactory _factory;
        private readonly AppDbContext _db;
        private readonly CacheOptions _options;
        private readonly MihonBridgeService _mihonBridgeService;


        public UrlImageProvider(ILogger<UrlImageProvider> logger, IHttpClientFactory factory, AppDbContext db, IOptions<CacheOptions> options, MihonBridgeService mihonBridgeService)
        {
            _logger = logger;
            _factory = factory;
            _db = db;
            _options = options.Value;
            _mihonBridgeService = mihonBridgeService;
        }

        public static async Task<string> ComputeMd5HashFromStreamAsync(Stream stream, CancellationToken token = default)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = await md5.ComputeHashAsync(stream, token).ConfigureAwait(false);
                return Convert.ToBase64String(hash);
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
                return string.Empty;
            }

            var trimmed = extension.Trim();
            return trimmed.StartsWith('.') ? trimmed : "." + trimmed.TrimStart('.');
        }
        public bool CanProcess(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            if (url.StartsWith("http://") || url.StartsWith("https://"))
                return true;
            return false;
        }
        public async Task<Stream?> ObtainStreamAsync(EtagCacheEntity cache, CancellationToken token)
        {
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
                    //try source
                    if (cache.MihonProviderId != null)
                    {

                        ISourceInterop interop = await _mihonBridgeService.SourceFromProviderIdAsync(cache.MihonProviderId).ConfigureAwait(false);
                        if (interop != null)
                        {
                            ContentTypeStream? image;
                            int retries = 2; //try twice with the interop in case of referer or missing headers.
                            do
                            {
                                string message = "Error downloading the image for {Key}";
                                if (retries == 2)
                                    message = "Error downloading the image for {Key}. Retrying...";
                                image = await _mihonBridgeService.MihonErrorWrapperAsync(
                                    () => interop.DownloadUrlAsync(cache.Url, token),
                                    message, cache.Key).ConfigureAwait(false);
                                if (image != null)
                                    break;
                            } while (retries-- > 0);
                            if (image == null)
                                return; //Warning already logged in the wrapper
                            await image.CopyToAsync(memoryStream, token).ConfigureAwait(false);
                        }
                        else
                        {
                            _logger.LogWarning("Error downloading the image for {Key}. Http error: {StatusCode}", cache.Key, response.StatusCode);
                            return;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Error downloading the image for {Key}. Http error: {StatusCode}", cache.Key, response.StatusCode);
                        return;
                    }
                }
                else
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
    }
}
