using RensaioBackend.Data;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Auth;
using RensaioBackend.Services.Images;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Providers;
using RensaioBackend.Services.Series;
using RensaioBackend.Services.Settings;
using RensaioBackend.Services.Status;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace RensaioBackend.Controllers
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
        private readonly AppDbContext _db;
        private readonly StatusEvaluationService _statusEvaluation;
        private readonly SettingsService _settings;

        public SeriesController(ILogger<SeriesController> logger,
            SeriesQueryService queryService,
            SeriesCommandService commandService,
            SeriesProviderService providerService,
            SeriesArchiveService archiveService,
            ThumbCacheService thumbCacheService,
            JobManagementService jobManagementService,
            AppDbContext db,
            StatusEvaluationService statusEvaluation,
            SettingsService settings)
        {
            _logger = logger;
            _queryService = queryService;
            _commandService = commandService;
            _providerService = providerService;
            _archiveService = archiveService;
            _thumb = thumbCacheService;
            _jobManagementService = jobManagementService;
            _db = db;
            _statusEvaluation = statusEvaluation;
            _settings = settings;
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
        public async Task<ActionResult<SeriesIntegrityResultDto>> VerifyIntegrityAsync([FromQuery] Guid g, [FromQuery] bool force = false, CancellationToken token = default)
        {
            try
            {
                var result = await _archiveService.VerifyIntegrityAsync(g, force, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying integrity: {Message}", ex.Message);
                return StatusCode(500, $"Error verifying integrity.");
            }
        }

        [HttpGet("cleanup")]
        [RequireUserLevel(UserLevel.Manager)]
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
        [RequireUserLevel(UserLevel.Manager)]
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
        /// Triggers an immediate metadata + new-chapter refresh for a single series.
        /// Re-fetches status, title, cover and description from each active provider.
        /// Paused series refresh metadata but do not download.
        /// </summary>
        /// <param name="id">The unique identifier of the series.</param>
        /// <param name="token">Cancellation token.</param>
        [HttpPost("refresh")]
        [RequireUserLevel(UserLevel.Manager)]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> RefreshSeriesAsync([FromQuery] Guid id, CancellationToken token = default)
        {
            try
            {
                if (id == Guid.Empty)
                    return BadRequest("No series id provided");
                int queued = await _commandService.RefreshSeriesMetadataAsync(id, token).ConfigureAwait(false);
                return Ok(new { success = true, queued });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing series: {Message}", ex.Message);
                return StatusCode(500, "Error refreshing series.");
            }
        }

        /// <summary>
        /// Gets the unified, series-level chapter list (merged across every source). Each chapter
        /// reports whether it is downloaded and from which source, versus genuinely missing, plus the
        /// sources available for (re-)download.
        /// </summary>
        /// <param name="seriesId">The unique identifier of the series.</param>
        /// <param name="token">Cancellation token.</param>
        [HttpGet("chapters")]
        [ProducesResponseType(typeof(List<ChapterDetailDto>), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<List<ChapterDetailDto>>> GetSeriesChaptersAsync([FromQuery] Guid seriesId, CancellationToken token = default)
        {
            try
            {
                if (seriesId == Guid.Empty)
                    return BadRequest("No series id provided");
                var result = await _queryService.GetSeriesChaptersAsync(seriesId, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting series chapters: {Message}", ex.Message);
                return StatusCode(500, "Error getting series chapters.");
            }
        }

        /// <summary>
        /// Re-downloads (or downloads) a single chapter, replacing any existing file. The source is
        /// resolved by priority (storage → current holder → any available) unless an explicit
        /// <paramref name="providerId"/> override is supplied. Blocked while the series is paused.
        /// </summary>
        /// <param name="seriesId">The series owning the chapter.</param>
        /// <param name="chapter">The chapter number to (re-)download.</param>
        /// <param name="providerId">Optional source to force; omit for the priority default.</param>
        /// <param name="token">Cancellation token.</param>
        [HttpPost("chapter/redownload")]
        [RequireUserLevel(UserLevel.Manager)]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> RedownloadChapterAsync([FromQuery] Guid seriesId, [FromQuery] decimal chapter, [FromQuery] Guid? providerId = null, CancellationToken token = default)
        {
            try
            {
                if (seriesId == Guid.Empty)
                    return BadRequest("No series id provided");

                RedownloadResult result = await _commandService.RedownloadChapterAsync(seriesId, chapter, providerId, token).ConfigureAwait(false);
                return result.Outcome switch
                {
                    RedownloadOutcome.Queued => Ok(new { success = true, queued = result.Queued, sourceProviderName = result.SourceProviderName }),
                    RedownloadOutcome.Paused => StatusCode(409, new { success = false, error = "Series is paused" }),
                    RedownloadOutcome.SeriesNotFound => NotFound(new { success = false, error = "Series not found" }),
                    RedownloadOutcome.ChapterNotFound => NotFound(new { success = false, error = "Chapter not found at source" }),
                    RedownloadOutcome.NoSourceAvailable => BadRequest(new { success = false, error = "No source available to download this chapter" }),
                    _ => StatusCode(500, new { success = false, error = "Error re-downloading chapter" })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error re-downloading chapter: {Message}", ex.Message);
                return StatusCode(500, "Error re-downloading chapter.");
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
        public async Task<ActionResult<List<LatestSeriesDto>>> GetLatestAsync([FromQuery] int start, [FromQuery] int count, [FromQuery] string? sourceId = null, [FromQuery] string? keyword = null, [FromQuery(Name = "genre")] string[]? genre = null, CancellationToken token = default)
        {
            try
            {
                var result = await _queryService.GetLatestAsync(start, count, sourceId, keyword, genre, token).ConfigureAwait(false);
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
        /// Gets the distinct tags/genres present in the cached "Latest" cloud catalogue,
        /// each with the number of series carrying it. Populates the browse-screen tag filter.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Distinct genres with their occurrence counts.</returns>
        [HttpGet("latest/genres")]
        [ProducesResponseType(typeof(List<LatestGenreDto>), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<List<LatestGenreDto>>> GetLatestGenresAsync(CancellationToken token = default)
        {
            try
            {
                var result = await _queryService.GetLatestGenresAsync(token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest genres: {Message}", ex.Message);
                return StatusCode(500, $"Error getting latest genres.");
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
                if (result?.MatchInfos!=null && result.MatchInfos.Count>0)
                {
                    await _thumb.PopulateThumbsAsync(result.MatchInfos, "/api/image/", token).ConfigureAwait(false);
                }
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
        [RequireUserLevel(UserLevel.Manager)]
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
        [RequireUserLevel(UserLevel.Manager)]
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

                // Import Series Wizard: sync ExternalMappings from rensaio.json into SeriesMappings
                // with the logged-in user's level for role-based overwrite protection
                if (HttpContext.Items["User"] is UserEntity user &&
                    series.LocalInfo?.Series.ExternalMappings?.Count > 0)
                {
                    await _commandService.SyncExternalMappingsFromSnapshotAsync(
                        seriesId, series.LocalInfo, user.Id, user.Level, token).ConfigureAwait(false);
                }

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
        [RequireUserLevel(UserLevel.Manager)]
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
                await _thumb.PopulateThumbsAsync(series.Providers, "/api/image/", token).ConfigureAwait(false);
                return Ok(series);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating series: {Message}", ex.Message);
                return StatusCode(500, $"Error updating series.");
            }
        }

        [HttpDelete]
        [RequireUserLevel(UserLevel.Admin)]
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
        /// <summary>
        /// Sets the release cadence for a series (user override).
        /// Stores as a negative value to indicate user-set, preventing auto-recalculation.
        /// Re-evaluates health alerts after updating the cadence.
        /// </summary>
        [HttpPatch("{id}/cadence")]
        [RequireUserLevel(UserLevel.Manager)]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> SetSeriesCadenceAsync(Guid id, [FromBody] SetCadenceRequest request, CancellationToken token = default)
        {
            string name = id.ToString();
            try
            {
                var series = await _db.Series.FirstOrDefaultAsync(s => s.Id == id, token).ConfigureAwait(false);
                if (series == null)
                    return NotFound(new { error = "Series not found" });
                name = series.Title;
                if (request.CadenceDays.HasValue)
                {
                    if (request.CadenceDays.Value <= 0)
                        return BadRequest(new { error = "Cadence must be greater than zero" });

                    // Store as negative to mark user-set (system will not auto-recalculate)
                    series.ReleaseCadenceDays = -Math.Abs(request.CadenceDays.Value);
                }
                else
                {
                    // Clear user override — allow system to recalculate
                    series.ReleaseCadenceDays = null;
                }

                await _db.SaveChangesAsync(token).ConfigureAwait(false);

                // Re-evaluate health alerts for this series with the new cadence
                var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                // We need to load the series with its Sources for evaluation
                var seriesWithSources = await _db.Series
                    .Include(s => s.Sources)
                    .FirstOrDefaultAsync(s => s.Id == id, token)
                    .ConfigureAwait(false);

                if (seriesWithSources != null)
                {
                    await _statusEvaluation.EvaluateSingleSeriesAsync(seriesWithSources, settings, token).ConfigureAwait(false);
                }

                return Ok(new
                {
                    releaseCadenceDays = series.ReleaseCadenceDays.HasValue
                        ? (int?)Math.Abs(series.ReleaseCadenceDays.Value)
                        : null,
                    isUserSet = series.ReleaseCadenceDays.HasValue && series.ReleaseCadenceDays.Value < 0,
                    message = "Cadence updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cadence for series {name}: {Message}", name, ex.Message);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
