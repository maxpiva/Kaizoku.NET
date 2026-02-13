using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Series;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/serie")]
    public class SeriesController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly SeriesQueryService _queryService;
        private readonly SeriesCommandService _commandService;
        private readonly SeriesProviderService _providerService;
        private readonly SeriesArchiveService _archiveService;
        private readonly ThumbCacheService _thumb;
        private readonly JobManagementService _jobManagementService;

        public SeriesController(ILogger<SeriesController> logger,
            SeriesQueryService queryService,
            SeriesCommandService commandService,
            SeriesProviderService providerService,
            SeriesArchiveService archiveService,
            ThumbCacheService thumbCacheService,
            JobManagementService jobManagementService)
        {
            _logger = logger;
            _queryService = queryService;
            _commandService = commandService;
            _providerService = providerService;
            _archiveService = archiveService;
            _thumb = thumbCacheService;
            _jobManagementService = jobManagementService;
        }

        /// <summary>
        /// Gets detailed information about a series by its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Extended information about the series.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(SeriesExtendedDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<SeriesExtendedDto>> GetSeriesAsync([FromQuery] Guid id, CancellationToken token = default)
        {
            try
            {
                var result = await _queryService.GetSeriesAsync(id, token).ConfigureAwait(false);
                await _thumb.PopulateThumbsAsync(result.Providers,"/api/image/", token).ConfigureAwait(false);
                await _thumb.PopulateThumbsAsync(result, "/api/image/", token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting series: {Message}", ex.Message);
                return StatusCode(500, $"Error getting series.");
            }
        }

        [HttpGet("verify")]
        [ProducesResponseType(typeof(SeriesIntegrityResultDto), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<SeriesIntegrityResultDto>> VerifyIntegrityAsync([FromQuery] Guid g, CancellationToken token = default)
        {
            try
            {
                var result = await _archiveService.VerifyIntegrityAsync(g, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying integrity: {Message}", ex.Message);
                return StatusCode(500, $"Error verifying integrity.");
            }
        }

        [HttpGet("cleanup")]
        [ProducesResponseType(500)]
        public async Task<ActionResult> CleanupSeriesAsync([FromQuery] Guid g, CancellationToken token = default)
        {
            try
            {
                await _archiveService.CleanupSeriesAsync(g, token).ConfigureAwait(false);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleanup series: {Message}", ex.Message);
                return StatusCode(500, $"Error cleanup series.");
            }
        }

        [HttpPost("update-all")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> UpdateAllSeriesAsync(CancellationToken token = default)
        {
            try
            {
                await _jobManagementService.EnqueueJobAsync(JobType.UpdateAllSeries, (string?)null, Priority.High, null, null, null, "Default", token).ConfigureAwait(false);
                return Ok(new { success = true, message = "Update All Series Queued" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error Updating All Series");
                return StatusCode(500, new { error = $"An error occurred during Error Updating All Series: {ex.Message}" });
            }
        }

        /// <summary>
        /// Gets the user's library of series.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of series in the library.</returns>
        [HttpGet("library")]
        [ProducesResponseType(typeof(List<SeriesInfoDto>), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<List<SeriesInfoDto>>> GetLibraryAsync(CancellationToken token = default)
        {
            try
            {
                var result = await _queryService.GetLibraryAsync(token).ConfigureAwait(false);
                await _thumb.PopulateThumbsAsync(result, "/api/image/", token).ConfigureAwait(false);
                await _thumb.PopulateThumbsAsync(result.SelectMany(a=>a.Providers).Where(a=>a!=null), "/api/image/", token).ConfigureAwait(false);
                await _thumb.PopulateThumbsAsync(result.Where(a=>a.LastChangeProvider!=null).Select(a=>a.LastChangeProvider), "/api/image/", token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting library: {Message}", ex.Message);
                return StatusCode(500, $"Error getting library.");
            }
        }

        [HttpGet("latest")]
        [ProducesResponseType(typeof(List<LatestSeriesDto>), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<List<LatestSeriesDto>>> GetLatestAsync([FromQuery] int start, [FromQuery] int count, [FromQuery] string? sourceId = null, [FromQuery] string? keyword = null, CancellationToken token = default)
        {
            try
            {
                var result = await _queryService.GetLatestAsync(start, count, sourceId, keyword, token).ConfigureAwait(false);
                await _thumb.PopulateThumbsAsync(result, "/api/image/", token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest cloud library: {Message}", ex.Message);
                return StatusCode(500, $"Error getting latest cloud library.");
            }
        }

        /// <summary>
        /// Gets a provider match by provider ID.
        /// </summary>
        /// <param name="providerId">The provider's unique identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provider match if found.</returns>
        [HttpGet("match/{providerId}")]
        [ProducesResponseType(typeof(ProviderMatchDto), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<ProviderMatchDto?>> GetMatchAsync([FromRoute] Guid providerId, CancellationToken token = default)
        {
            try
            {
                var result = await _providerService.GetMatchAsync(providerId, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting provider match: {Message}", ex.Message);
                return StatusCode(500, $"Error getting provider match.");
            }
        }

        /// <summary>
        /// Sets a provider match.
        /// </summary>
        /// <param name="pmatch">The provider match object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the match was set successfully.</returns>
        [HttpPost("match")]
        [ProducesResponseType(typeof(bool), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<bool>> SetMatchAsync([FromBody] ProviderMatchDto pmatch, CancellationToken token = default)
        {
            try
            {
                if (pmatch == null)
                    return BadRequest("No provider match provided");
                var result = await _providerService.SetMatchAsync(pmatch, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting provider match: {Message}", ex.Message);
                return StatusCode(500, $"Error setting provider match.");
            }
        }

        /// <summary>
        /// Add a series with full details directly to the database.
        /// </summary>
        /// <param name="series">List of full series with complete information to add.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ID of the newly created series.</returns>
        [HttpPost]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> AddSeriesAsync([FromBody] AugmentedResponseDto series, CancellationToken token = default)
        {
            try
            {
                if (series == null || series.Series == null || series.Series.Count == 0)
                {
                    return BadRequest("No series provided to add");
                }

                var seriesId = await _commandService.AddSeriesAsync(series, token).ConfigureAwait(false);
                return Ok(new { id = seriesId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding full series: {Message}", ex.Message);
                return StatusCode(500, $"Error adding full series.");
            }
        }

        /// <summary>
        /// Update a series with full details directly to the database.
        /// </summary>
        /// <param name="series">Series with complete information to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated series information.</returns>
        [HttpPatch]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<SeriesExtendedDto>> UpdateSeriesAsync([FromBody] SeriesExtendedDto series, CancellationToken token = default)
        {
            try
            {
                if (series == null)
                {
                    return BadRequest("No series provided to update");
                }

                series = await _commandService.UpdateSeriesAsync(series, token).ConfigureAwait(false);
                await _thumb.PopulateThumbsAsync(series, "/api/image/", token).ConfigureAwait(false);
                return Ok(series);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating series: {Message}", ex.Message);
                return StatusCode(500, $"Error updating series.");
            }
        }

        [HttpDelete]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> DeleteSeriesAsync([FromQuery] Guid id, [FromQuery] bool alsoPhysical = false, CancellationToken token = default)
        {
            try
            {
                await _commandService.DeleteSeriesAsync(id, alsoPhysical, token).ConfigureAwait(false);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting series: {Message}", ex.Message);
                return StatusCode(500, $"Error updating series with id {id}");
            }
        }
    }
}
