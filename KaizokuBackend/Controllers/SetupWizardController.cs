using KaizokuBackend.Data;
using KaizokuBackend.Hubs;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Runtime;

namespace KaizokuBackend.Controllers
{
    /// <summary>
    /// Controller for handling setup wizard operations
    /// </summary>
    [ApiController]
    [Route("api/setup")]
    [Produces("application/json")]
    public class SetupWizardController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly AppDbContext _db;
        private readonly JobManagementService _jobManagementService;
        private readonly ImportQueryService _importQueryService;
        private readonly ImportCommandService _importCommandService;
        private readonly SettingsService _settings;
        public SetupWizardController(ILogger<SetupWizardController> logger,
            JobManagementService jobManagementService,
            AppDbContext db,
            ImportQueryService importQueryService,
            ImportCommandService importCommandService,
            SettingsService settings)
        {
            _jobManagementService = jobManagementService;
            _db = db;
            _settings = settings;
            _logger = logger;
            _importQueryService = importQueryService;
            _importCommandService = importCommandService;
        }

        /// <summary>
        /// Scan local files for series
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status of the scan operation</returns>
        /// <response code="200">Scan completed successfully</response>
        /// <response code="400">If directory path is invalid</response>
        /// <response code="500">If an error occurs during scanning</response>
        [HttpPost("scan")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> ScanLocalFilesAsync(CancellationToken token = default)
        {
            try
            {
                Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                await _jobManagementService.EnqueueJobAsync(JobType.ScanLocalFiles, settings.StorageFolder, Priority.High, null, null, null, "Default", token).ConfigureAwait(false);
                return Ok(new { success = true, message = "Scan Scheduled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scheduling scan");
                return StatusCode(500, new { error = $"An error occurred during scheduling: {ex.Message}" });
            }
        }

        /// <summary>
        /// Install additional extensions required for the imported series
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status of the installation operation</returns>
        /// <response code="200">Extensions installed successfully</response>
        /// <response code="500">If an error occurs during installation</response>
        [HttpPost("install-extensions")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> InstallAdditionalExtensionsAsync(CancellationToken token = default)
        {
            try
            {
                await _jobManagementService.EnqueueJobAsync<string?>(JobType.InstallAdditionalExtensions, null, Priority.High, null, null, null, "Default", token).ConfigureAwait(false);
                return Ok(new { success = true, message = "Install Extensions Scheduled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scheduling Install Extensions");
                return StatusCode(500, new { error = $"An error occurred during scheduling: {ex.Message}" });
            }
        }

        /// <summary>
        /// Augment imported series with additional information.
        /// </summary>
        /// <param name="path">Path to the import folder.</param>
        /// <param name="linkedSeries">List of linked series to augment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Augmented import information.</returns>
        /// <response code="200">Augmentation completed successfully</response>
        /// <response code="400">If no series were provided</response>
        /// <response code="500">If an error occurs during augmentation</response>
        [HttpPost("augment")]
        [ProducesResponseType(typeof(ImportInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ImportInfo>> AugmentAsync([FromQuery] string path, [FromBody] List<LinkedSeries> linkedSeries, CancellationToken token = default)
        {
            try
            {
                if (linkedSeries == null || linkedSeries.Count == 0)
                {
                    return BadRequest(new { error = "No series provided to augment" });
                }
                return Ok(await _importQueryService.AugmentAsync(path, linkedSeries, token).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error augmenting series: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while augmenting series." });
            }
        }

        /// <summary>
        /// Update import information.
        /// </summary>
        /// <param name="info">Import information to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status of the update operation.</returns>
        /// <response code="200">Update completed successfully</response>
        /// <response code="400">If no import was provided</response>
        /// <response code="500">If an error occurs during update</response>
        [HttpPost("update")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> UpdateAsync([FromBody] ImportInfo info, CancellationToken token = default)
        {
            try
            {
                if (info == null)
                {
                    return BadRequest(new { error = "No Import provided" });
                }
                await _importCommandService.UpdateImportInfoAsync(info, token).ConfigureAwait(false);
                return Ok(new { success = true, message = "Import updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating Import: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while updating Import" });
            }
        }

        /// <summary>
        /// Search for series based on imported content
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status of the search operation</returns>
        /// <response code="200">Search completed successfully</response>
        /// <response code="500">If an error occurs during search</response>
        [HttpPost("search")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> SearchSeriesAsync(CancellationToken token = default)
        {
            try
            {
                await _jobManagementService.EnqueueJobAsync<string?>(JobType.SearchProviders, null, Priority.High, null, null, null, "Default", token).ConfigureAwait(false);
                return Ok(new { success = true, message = "Search Series Scheduled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scheduling Search Series");
                return StatusCode(500, new { error = $"An error occurred during scheduling: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get list of pending imports
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of pending imports</returns>
        /// <response code="200">Returns the list of imports</response>
        /// <response code="500">If an error occurs retrieving imports</response>
        [HttpGet("imports")]
        [ProducesResponseType(typeof(List<ImportInfo>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<List<ImportInfo>>> GetImportsAsync(CancellationToken token = default)
        {
            try
            {
                return Ok(await _importQueryService.GetImportsAsync(token).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving imports");
                return StatusCode(500, new { error = $"An error occurred retrieving imports: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get list of pending imports
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of pending imports</returns>
        /// <response code="200">Returns the list of imports</response>
        /// <response code="500">If an error occurs retrieving imports</response>
        [HttpGet("imports/totals")]
        [ProducesResponseType(typeof(ImportTotals), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ImportTotals>> GetImportsTotalsAsync(CancellationToken token = default)
        {
            try
            {
                return Ok(await _importQueryService.GetImportsTotalsAsync(token).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving imports totals");
                return StatusCode(500, new { error = $"An error occurred retrieving imports totals: {ex.Message}" });
            }
        }

        /// <summary>
        /// Import series from the provided list
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status of the import operation</returns>
        /// <response code="200">Import completed successfully</response>
        /// <response code="500">If an error occurs during import</response>
        [HttpPost("import")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> ImportSeriesAsync([FromQuery] bool disableDownloads, CancellationToken token = default)
        {
            try
            {
                await _jobManagementService.EnqueueJobAsync<bool>(JobType.ImportSeries, disableDownloads, Priority.High, null, null, null,"Default", token).ConfigureAwait(false);
                return Ok(new { success = true, message = "Import Series Scheduled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scheduling Import Series");
                return StatusCode(500, new { error = $"An error occurred during scheduling: {ex.Message}" });
            }
        }
    }
}
