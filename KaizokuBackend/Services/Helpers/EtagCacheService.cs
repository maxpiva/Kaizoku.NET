using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using KaizokuBackend.Extensions;

namespace KaizokuBackend.Services.Helpers
{
    /// <summary>
    /// Service to handle ETag cache operations for resource change detection
    /// </summary>
    public class EtagCacheService
    {
        private readonly AppDbContext _db;
        private readonly ILogger _logger;
        private readonly ContextProvider _context;
        public EtagCacheService(ILogger<EtagCacheService> logger,
            AppDbContext db, 
            ContextProvider context)
        {
            _db = db;
            _logger = logger;
            _context = context;
        }

        /// <summary>
        /// Checks if the provided ETag matches the one stored for the given key
        /// </summary>
        /// <param name="key">The unique identifier for the resource</param>
        /// <param name="etag">The ETag to check against</param>
        /// <returns>True if the ETag matches, false otherwise</returns>
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
  
        /// <summary>
        /// Updates or creates an ETag entry for the given key based on the provided data stream
        /// </summary>
        /// <param name="key">The unique identifier for the resource</param>
        /// <param name="data">The data stream to compute an MD5 hash from</param>
        /// <returns>The new ETag value</returns>
        public async Task<string> UpdateETagAsync(string key, Stream data, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(key) || data == null)
            {
                throw new ArgumentException("Key and data stream must be provided");
            }

            try
            {
                // Compute MD5 hash from the stream
                string etag = await ComputeMd5HashFromStreamAsync(data, token).ConfigureAwait(false);
                
                // Reset the stream position if possible
                if (data.CanSeek)
                {
                    data.Position = 0;
                }

                // Check if entry exists
                var cacheEntry = await _db.ETagCache.FirstOrDefaultAsync(e => e.Key == key).ConfigureAwait(false);

                if (cacheEntry != null && cacheEntry.Etag!=etag)
                {
                    // Update existing entry
                    cacheEntry.Etag = etag;
                    cacheEntry.LastUpdated = DateTime.UtcNow;
                    await _db.SaveChangesAsync().ConfigureAwait(false);
                }
                else if (cacheEntry==null)
                {
                    // Create new entry
                    cacheEntry = new EtagCache
                    {
                        Key = key,
                        Etag = etag,
                        LastUpdated = DateTime.UtcNow
                    };
                    _db.ETagCache.Add(cacheEntry);
                    await _db.SaveChangesAsync().ConfigureAwait(false);
                }

                return etag;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating ETag for key {key}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Computes an MD5 hash from a data stream
        /// </summary>
        /// <param name="stream">The data stream to hash</param>
        /// <returns>The MD5 hash as a base64 string</returns>
        private async Task<string> ComputeMd5HashFromStreamAsync(Stream stream, CancellationToken token = default)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = await md5.ComputeHashAsync(stream, token).ConfigureAwait(false);
                return Convert.ToBase64String(hash);
            }
        }



        public async Task<IActionResult> ETagWrapperAsync(string key, Func<Task<Stream>> streamProvider, CancellationToken token = default)
        {
            (HttpStatusCode statusCode, string? etag, string? mimetype, Stream? stream) = await CheckImageAsync(key, streamProvider, token).ConfigureAwait(false);
            if (statusCode == HttpStatusCode.NotModified)
            {
                return new StatusCodeResult((int)HttpStatusCode.NotModified);
            }

            if (stream == null || stream.Length == 0)
            {
                return new NotFoundResult();
            }
            if (stream != null && etag != null)
            {
                _context.AddETag(etag);
                return new FileStreamResult(stream, mimetype ?? "application/octet-stream");
            }

            if (stream != null)
            {
                return new FileStreamResult(stream, mimetype ?? "application/octet-stream");
            }
            return new StatusCodeResult((int)HttpStatusCode.NotFound);
        }

        public async Task<(HttpStatusCode StatusCode, string? etag, string? mimetype, Stream? stream)> CheckImageAsync(string key, Func<Task<Stream>> streamProvider, CancellationToken token = default)
        {
            string? etag = _context.GetETagFromRequest();
            if (string.IsNullOrEmpty(key))
            {
                return (HttpStatusCode.BadRequest, null, null, null);
            }
            // Check ETag
            if (!string.IsNullOrEmpty(etag) && (await CheckETagAsync(key, etag, token).ConfigureAwait(false)))
            {
                return (HttpStatusCode.NotModified, null, null , null);
            }
            // Update ETag
            using (Stream stream = await streamProvider().ConfigureAwait(false))
            {
                MemoryStream ms = new MemoryStream();
                await stream.CopyToAsync(ms).ConfigureAwait(false);
                ms.Position = 0;
                (string? mime, _) = ms.GetImageMimeTypeAndExtension();
                string newEtag = await UpdateETagAsync(key, ms, token).ConfigureAwait(false);
                return (HttpStatusCode.OK, newEtag, mime, ms);
            }
        }
    }
}