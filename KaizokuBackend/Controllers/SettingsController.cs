
using Microsoft.AspNetCore.Mvc;
using KaizokuBackend.Models;
using System.ComponentModel.DataAnnotations;
using KaizokuBackend.Services.Settings;

namespace KaizokuBackend.Controllers
{
    /// <summary>
    /// Controller for managing application settings
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    [Produces("application/json")]
    public class SettingsController : ControllerBase
    {
        private readonly SettingsService _settingsService;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(SettingsService settingsService, ILogger<SettingsController> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        /// <summary>
        /// Gets the current application settings.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current settings.</returns>
        /// <response code="200">Returns the current settings</response>
        /// <response code="500">If an error occurs while retrieving settings</response>
        [HttpGet]
        [ProducesResponseType(typeof(Settings), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<Settings>> GetAsync(CancellationToken token = default)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync(token).ConfigureAwait(false);
                return Ok(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while retrieving settings" });
            }
        }

        /// <summary>
        /// Gets the available languages from all sources.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Array of language codes supported by sources.</returns>
        /// <response code="200">Returns the available languages</response>
        /// <response code="500">If an error occurs while retrieving languages</response>
        [HttpGet("languages")]
        [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<string[]>> GetAvailableLanguagesAsync(CancellationToken token = default)
        {
            try
            {
                var languages = await _settingsService.GetAvailableLanguagesAsync(token).ConfigureAwait(false);
                return Ok(languages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving available languages");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while retrieving available languages" });
            }
        }

        /// <summary>
        /// Updates application settings.
        /// </summary>
        /// <param name="settings">The settings to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status of the update operation.</returns>
        /// <response code="200">Settings updated successfully</response>
        /// <response code="400">If the settings are invalid</response>
        /// <response code="500">If an error occurs during update</response>
        [HttpPut]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateAsync([FromBody][Required] Settings settings, CancellationToken token = default)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { errors = ModelState });
            }

            try
            {
                await _settingsService.SaveSettingsAsync(settings, false, token).ConfigureAwait(false);
                return Ok(new { message = "Settings updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while updating settings" });
            }
        }
    }
}