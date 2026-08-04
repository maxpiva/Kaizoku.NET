
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using RensaioBackend.Data;
using RensaioBackend.Models.Database;
using RensaioBackend.Services.Settings;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace RensaioBackend.Controllers
{
    /// <summary>
    /// Response DTO for settings update, may include a set-password redirect URL
    /// when authentication is enabled but the current user has no password set.
    /// </summary>
    public class SettingsUpdateResponseDto
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = "Settings saved successfully";

        [JsonPropertyName("setPasswordUrl")]
        public string? SetPasswordUrl { get; set; }
    }

    /// <summary>
    /// Controller for managing application settings
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    [Produces("application/json")]
    public class SettingsController : ControllerBase
    {
        private readonly SettingsService _settingsService;
        private readonly AppDbContext _db;
        private readonly UserInviteService _userInviteService;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            SettingsService settingsService,
            AppDbContext db,
            UserInviteService userInviteService,
            ILogger<SettingsController> logger)
        {
            _settingsService = settingsService;
            _db = db;
            _userInviteService = userInviteService;
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
        [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SettingsDto>> GetAsync(CancellationToken token = default)
        {
            try
            {
                var settings = await _settingsService.GetMaskedSettingsAsync(token).ConfigureAwait(false);
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
        [RequireUserLevel(UserLevel.Owner)]
        [ProducesResponseType(typeof(SettingsUpdateResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SettingsUpdateResponseDto>> UpdateAsync([FromBody][Required] SettingsDto settings, CancellationToken token = default)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { errors = ModelState });
            }

            try
            {
                // Get the current settings before saving to detect if auth is being enabled
                var currentSettings = await _settingsService.GetSettingsAsync(token).ConfigureAwait(false);
                bool authWasEnabled = currentSettings.AuthenticationEnabled;
                bool authNowEnabled = settings.AuthenticationEnabled;

                // Save the new settings first
                await _settingsService.SaveSettingsAsync(settings, false, token).ConfigureAwait(false);

                var response = new SettingsUpdateResponseDto();

                // If auth is being enabled now (was disabled before), check if current user needs a password
                if (!authWasEnabled && authNowEnabled)
                {
                    UserEntity? user = HttpContext.Items["User"] as UserEntity;

                    if (user != null && string.IsNullOrWhiteSpace(user.PasswordHash))
                    {
                        // User has no password — generate a set-password token and URL
                        _userInviteService.GeneratePasswordSetToken(user);
                        // The user entity was originally loaded by the AuthMiddleware's scoped DbContext,
                        // not this controller's _db, so we must explicitly attach it for tracking.
                        _db.Entry(user).State = EntityState.Modified;
                        await _db.SaveChangesAsync(token).ConfigureAwait(false);

                        string externalDomain = string.IsNullOrWhiteSpace(settings.ExternalDomain)
                            ? $"http://localhost:9833"
                            : settings.ExternalDomain;

                        string cleanDomain = externalDomain.TrimEnd('/');
                        response.SetPasswordUrl = $"{cleanDomain}/auth/set-password?username={Uri.EscapeDataString(user.Username)}&token={user.PasswordSetToken}";
                        response.Message = "Authentication enabled. You must set a password to log in.";
                    }
                }

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while updating settings" });
            }
        }
    }
}