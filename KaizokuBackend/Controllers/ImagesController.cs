using com.sun.xml.@internal.bind.v2.model.core;
using KaizokuBackend.Extensions;
using KaizokuBackend.Services.Downloads;
using KaizokuBackend.Services.Images;
using KaizokuBackend.Services.Images.Providers;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/image")]
    [Produces("image/png","image/jpeg","image/gif","image/bmp","image/tiff","image/webp","image/jxl","image/jp2","image/avif","image/heic")]
    public class ImagesController : ControllerBase
    {
        private readonly ThumbCacheService _thumbs;
        private readonly ILogger _logger;
        private static string naetag=null;


        public ImagesController(ILogger<ImagesController> logger, ThumbCacheService thumbs)
        {
            _thumbs = thumbs;
            _logger = logger;
        }
        [HttpGet("{key?}")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetImageAsync([FromRoute] string key, CancellationToken token)
        {
            try
            {
                string? etag = Request.GetETagFromRequest();
                if (string.IsNullOrWhiteSpace(key) || key == "unknown")
                {
                    if (etag != null && naetag != null && etag == naetag)
                    {
                        return StatusCode(StatusCodes.Status304NotModified);
                    }
                    Stream fs = FileSystemExtensions.StreamEmbeddedResource("na.jpg");
                    if (naetag == null)
                    {
                        naetag = await UrlImageProvider.ComputeMd5HashFromStreamAsync(fs, token).ConfigureAwait(false);
                        fs.Position = 0;
                    }
                    Response.AddETag(_thumbs.GetCacheDuration(), naetag);
                    return File(fs, "image/jpeg");
                }
                var result = await _thumbs.ProcessKeyAsync(key, etag ?? string.Empty, token).ConfigureAwait(false);
                if (result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    Response.AddETag(_thumbs.GetCacheDuration(), result.etag!);
                    return File(result.stream ?? new MemoryStream(), result.mimetype ?? "application/octet-stream");
                }
                return StatusCode((int)result.StatusCode);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("GetImageAsync operation was cancelled for key: {Key}", key);
                return StatusCode(StatusCodes.Status500InternalServerError, "Operation was cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving image for key: {Key}", key);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving the image.");
            }
        }
    }
}
