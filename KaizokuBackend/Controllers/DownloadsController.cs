using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Downloads;
using KaizokuBackend.Services.Images;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/downloads")]
    [Produces("application/json")]
    public class DownloadsController : ControllerBase
    {
        private readonly DownloadQueryService _downloadQuery;
        private readonly DownloadCommandService _downloadCommand;
        private readonly ThumbCacheService _thumbs;
        private readonly ILogger _logger;

        public DownloadsController(ILogger<DownloadsController> logger,
            ThumbCacheService thumbs,
            DownloadQueryService downloadQuery, 
            DownloadCommandService downloadCommand)
        {
            _downloadQuery = downloadQuery;
            _downloadCommand = downloadCommand;
            _thumbs = thumbs;
            _logger = logger;
        }

        [HttpGet("series")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<List<DownloadInfoDto>>> GetDownloadsForSeriesAsync([FromQuery] Guid seriesId, CancellationToken token = default)
        {
            try
            {
                var sources = await _downloadQuery.GetDownloadsForSeriesAsync(seriesId, token).ConfigureAwait(false);
                await _thumbs.PopulateThumbsAsync(sources, "/api/image/", token).ConfigureAwait(false);
                return Ok(sources);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving downloads for Series: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while retrieving downloads for Series" });
            }
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<DownloadInfoListDto>> GetDownloadsAsync([FromQuery] QueueStatus status, int limit = 100, string? keyword = null, CancellationToken token = default)
        {
            try
            {
                var result = await _downloadQuery.GetDownloadsAsync(status, limit, keyword, token).ConfigureAwait(false);
                await _thumbs.PopulateThumbsAsync(result.Downloads, "/api/image/", token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving downloads for Series: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while retrieving downloads for Series" });
            }
        }
        
        [HttpGet("metrics")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<DownloadsMetricsDto>> GetDownloadsMetricsAsync(CancellationToken token = default)
        {
            try
            {
                var result = await _downloadQuery.GetDownloadsMetricsAsync(token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving downloads metrics: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while retrieving downloads metrics" });
            }
        }
        
        [HttpPatch]
        public async Task<ActionResult> ManageErrorDownloadAsync([FromQuery]Guid id, [FromQuery]ErrorDownloadAction action, CancellationToken token = default)
        {
            try
            {
                await _downloadCommand.ManageErrorDownloadAsync(id, action, token).ConfigureAwait(false);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error managing download: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while managing the download." });
            }
        }

        /// <summary>
        /// Remove a single download from the queue (waiting, completed, or failed)
        /// </summary>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> RemoveDownloadAsync(Guid id, CancellationToken token = default)
        {
            try
            {
                bool removed = await _downloadCommand.RemoveDownloadAsync(id, token).ConfigureAwait(false);
                return removed ? Ok() : NotFound(new { error = "Download not found or is currently running." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing download {Id}: {Message}", id, ex.Message);
                return StatusCode(500, new { error = "An error occurred while removing the download." });
            }
        }

        /// <summary>
        /// Clear all downloads with a given status
        /// </summary>
        [HttpDelete("clear")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<int>> ClearDownloadsByStatusAsync([FromQuery] QueueStatus status, CancellationToken token = default)
        {
            try
            {
                if (status == QueueStatus.Running)
                    return BadRequest(new { error = "Cannot clear running downloads." });

                int count = await _downloadCommand.ClearDownloadsByStatusAsync(status, token).ConfigureAwait(false);
                return Ok(new { cleared = count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing downloads with status {Status}: {Message}", status, ex.Message);
                return StatusCode(500, new { error = "An error occurred while clearing downloads." });
            }
        }

        /// <summary>
        /// Retry all failed downloads
        /// </summary>
        [HttpPost("retry-all")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<int>> RetryAllFailedDownloadsAsync(CancellationToken token = default)
        {
            try
            {
                int count = await _downloadCommand.RetryAllFailedDownloadsAsync(token).ConfigureAwait(false);
                return Ok(new { retried = count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying failed downloads: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while retrying failed downloads." });
            }
        }
    }
}
