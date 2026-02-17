using KaizokuBackend.Models.Dto;
using KaizokuBackend.Services.Images;
using KaizokuBackend.Services.Providers;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/provider")]
    [Produces("application/json")]
    public class ProviderController : ControllerBase
    {
        private readonly ProviderManagerService _managerService;
        private readonly ProviderPreferencesService _preferencesService;
        private readonly ThumbCacheService _thumbs;
        private readonly ILogger _logger;

        public ProviderController(
            ILogger<ProviderController> logger,
            ThumbCacheService thumbs,
            ProviderManagerService installationService,
            ProviderPreferencesService preferencesService)
        {
            _logger = logger;
            _thumbs = thumbs;
            _managerService = installationService;
            _preferencesService = preferencesService;
        }

        /// <summary>
        /// Gets a list of all available extensions (installed and available to install)
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of extensions</returns>
        /// <response code="200">Returns the list of extensions</response>
        /// <response code="500">If an error occurs while retrieving extensions</response>
        [HttpGet("list")]
        [ProducesResponseType(typeof(List<ExtensionDto>), 200)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<ActionResult<List<ExtensionDto>>> GetProvidersAsync(CancellationToken token = default)
        {
            try
            {
                var extensions = await _managerService.GetProvidersAsync(token).ConfigureAwait(false);
                await _thumbs.PopulateThumbsAsync(extensions, "/api/image/", token).ConfigureAwait(false);
                return Ok(extensions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving extensions");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Installs an extension by package name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Success status</returns>
        /// <response code="200">Extension installed successfully</response>
        /// <response code="400">Failed to install extension</response>
        /// <response code="500">If an error occurs during installation</response>
        [HttpPost("install/{pkgName}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<IActionResult> InstallProvider([FromRoute] string pkgName, [FromQuery] string? repoName = null, [FromQuery] bool force = false, CancellationToken token = default)
        {
            try
            {
                var success = await _managerService.InstallProviderAsync(pkgName, repoName, force, token).ConfigureAwait(false);
                if (success)
                {
                    return Ok(new { message = "Extension installed successfully" });
                }
                return BadRequest(new { error = "Failed to install extension" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error installing extension {pkgName}", pkgName);
                return StatusCode(500, new { error = ex.Message });
            }
        }



        /// <summary>
        /// Gets the preferences for a provider extension
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Provider preferences</returns>
        /// <response code="200">Returns the provider preferences</response>
        /// <response code="400">Provider not found</response>
        /// <response code="500">If an error occurs while retrieving preferences</response>
        [HttpGet("preferences/{pkgName}")]
        [ProducesResponseType(typeof(ProviderPreferencesDto), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<ActionResult<ProviderPreferencesDto>> GetPreferencesAsync([FromRoute] string pkgName, CancellationToken token = default)
        {
            try
            {
                var prefs = await _preferencesService.GetProviderPreferencesAsync(pkgName, token).ConfigureAwait(false);
                if (prefs != null)
                {
                    return Ok(prefs);
                }
                return BadRequest(new { error = "Provider not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting preference of {pkgName}", pkgName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Sets the preferences for a provider extension
        /// </summary>
        /// <param name="prefs">Provider preferences object</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status of the operation</returns>
        /// <response code="200">Preferences set successfully</response>
        /// <response code="500">If an error occurs while setting preferences</response>
        [HttpPost("preferences")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<IActionResult> SetPreferencesAsync([FromBody] ProviderPreferencesDto prefs, CancellationToken token = default)
        {
            try
            {
                await _preferencesService.SetProviderPreferencesAsync(prefs, token).ConfigureAwait(false);
                return Ok(new { message = "Preferences set successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting preferences for {PkgName}", prefs.PkgName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Disables an extension by package name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Success status</returns>
        /// <response code="200">Extension uninstalled successfully</response>
        /// <response code="400">Failed to uninstall extension</response>
        /// <response code="500">If an error occurs during uninstallation</response>
        [HttpPost("uninstall/{pkgName}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<IActionResult> DisableProviderAsync([FromRoute] string pkgName, CancellationToken token = default)
        {
            try
            {
                var success = await _managerService.DisableProviderAsync(pkgName, token).ConfigureAwait(false);
                if (success)
                {
                    return Ok(new { message = "Extension disabled successfully" });
                }
                return BadRequest(new { error = "Failed to disabled extension" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling extension {pkgName}", pkgName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Installs an extension from an uploaded file
        /// </summary>
        /// <param name="file">The extension file to upload</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Success status</returns>
        /// <response code="200">Extension installed successfully</response>
        /// <response code="400">Failed to install extension</response>
        /// <response code="500">If an error occurs during installation</response>
        [HttpPost("install/file")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<ActionResult<string>> InstallProviderFromFileAsync([FromForm] IFormFile file, [FromQuery] bool force = false, CancellationToken token = default)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded" });
            }

            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, token).ConfigureAwait(false);
                var content = ms.ToArray();
                string? pkgName = await _managerService.InstallProviderFromFileAsync(content, force, token).ConfigureAwait(false);
                if (pkgName != null)
                    return Ok(pkgName);
                return BadRequest(new { error = "Failed to install extension" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error installing extension from file {FileName}", file?.FileName);
                return StatusCode(500, new { error =$"Error installing extension from file {file?.FileName ?? ""}."});
            }
        }
    }
}
