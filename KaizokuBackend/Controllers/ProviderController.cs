using Microsoft.AspNetCore.Mvc;
using KaizokuBackend.Models;
using KaizokuBackend.Services.Providers;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/provider")]
    [Produces("application/json")]
    public class ProviderController : ControllerBase
    {
        private readonly ProviderQueryService _queryService;
        private readonly ProviderInstallationService _installationService;
        private readonly ProviderPreferencesService _preferencesService;
        private readonly ProviderResourceService _resourceService;
        private readonly ILogger _logger;

        public ProviderController(
            ILogger<ProviderController> logger, 
            ProviderQueryService queryService,
            ProviderInstallationService installationService,
            ProviderPreferencesService preferencesService,
            ProviderResourceService resourceService)
        {
            _logger = logger;
            _queryService = queryService;
            _installationService = installationService;
            _preferencesService = preferencesService;
            _resourceService = resourceService;
        }

        /// <summary>
        /// Gets a list of all available extensions (installed and available to install)
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of extensions</returns>
        /// <response code="200">Returns the list of extensions</response>
        /// <response code="500">If an error occurs while retrieving extensions</response>
        [HttpGet("list")]
        [ProducesResponseType(typeof(List<SuwayomiExtension>), 200)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<ActionResult<List<SuwayomiExtension>>> GetProvidersAsync(CancellationToken token = default)
        {
            try
            {
                var extensions = await _queryService.GetProvidersAsync(token).ConfigureAwait(false);
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
        public async Task<IActionResult> InstallProvider([FromRoute] string pkgName, CancellationToken token = default)
        {
            try
            {
                var success = await _installationService.InstallProviderAsync(pkgName, token).ConfigureAwait(false);
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
        [ProducesResponseType(typeof(ProviderPreferences), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<ActionResult<ProviderPreferences>> GetPreferencesAsync([FromRoute] string pkgName, CancellationToken token = default)
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
        public async Task<IActionResult> SetPreferencesAsync([FromBody] ProviderPreferences prefs, CancellationToken token = default)
        {
            try
            {
                await _preferencesService.SetProviderPreferencesAsync(prefs, token).ConfigureAwait(false);
                return Ok(new { message = "Preferences set successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting preferences for {ApkName}", prefs.ApkName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Uninstalls an extension by package name
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
        public async Task<IActionResult> UninstallProviderAsync([FromRoute] string pkgName, CancellationToken token = default)
        {
            try
            {
                var success = await _installationService.UninstallProviderAsync(pkgName, token).ConfigureAwait(false);
                if (success)
                {
                    return Ok(new { message = "Extension uninstalled successfully" });
                }
                return BadRequest(new { error = "Failed to uninstall extension" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uninstalling extension {pkgName}", pkgName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Gets the icon for an extension
        /// </summary>
        /// <param name="apkName">APK name of the extension</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Extension icon</returns>
        /// <response code="200">Returns the extension icon</response>
        /// <response code="500">If an error occurs while retrieving the icon</response>
        [HttpGet("icon/{apkName}")]
        [ProducesResponseType(typeof(FileResult), 200)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<IActionResult> GetExtensionIcon([FromRoute] string apkName, CancellationToken token = default)
        {
            try
            {
                return await _resourceService.GetProviderIconAsync(apkName, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting icon for {apkName}", apkName);
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
        public async Task<ActionResult<string>> InstallProviderFromFileAsync([FromForm] IFormFile file, CancellationToken token = default)
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
                string? apkName = await _installationService.InstallProviderFromFileAsync(content, file.FileName, token).ConfigureAwait(false);
                if (apkName!=null)
                    return Ok(apkName);
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
