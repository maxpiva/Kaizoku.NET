using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Downloads;
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
        private readonly ILogger _logger;

        public DownloadsController(ILogger<DownloadsController> logger, DownloadQueryService downloadQuery, DownloadCommandService downloadCommand)
        {
            _downloadQuery = downloadQuery;
            _downloadCommand = downloadCommand;
            _logger = logger;
        }

        [HttpGet("series")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<List<DownloadInfo>>> GetDownloadsForSeriesAsync([FromQuery] Guid seriesId, CancellationToken token = default)
        {
            try
            {
                var sources = await _downloadQuery.GetDownloadsForSeriesAsync(seriesId, token).ConfigureAwait(false);
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
        public async Task<ActionResult<DownloadInfoList>> GetDownloadsAsync([FromQuery] QueueStatus status, int limit = 100, string? keyword = null, CancellationToken token = default)
        {
            try
            {
                var result = await _downloadQuery.GetDownloadsAsync(status, limit, keyword, token).ConfigureAwait(false);
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
        public async Task<ActionResult<DownloadsMetrics>> GetDownloadsMetricsAsync(CancellationToken token = default)
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
    }
}
