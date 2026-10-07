
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Models.Database;
using RensaioBackend.Services.Settings;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Auth;
using RensaioBackend.Services.Contributions;
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
        private readonly ContributionPropagationService _contributionPropagation;

        public SettingsController(
            SettingsService settingsService,
            AppDbContext db,
            UserInviteService userInviteService,
            ILogger<SettingsController> logger,
            ContributionPropagationService contributionPropagation)
        {
            _settingsService = settingsService;
            _db = db;
            _userInviteService = userInviteService;
            _logger = logger;
            _contributionPropagation = contributionPropagation;
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
                // Every logged-in user can read settings; the OIDC client secret is blanked.
                var settings = await _settingsService.GetSettingsForClientAsync(token).ConfigureAwait(false);
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

                // If contribution is enabled with a Contributor Id, re-verify it against
                // the cloud contribution DB so the verified flag stays truthful. When
                // contribution is disabled or the Id is blank, clear the verified flag.
                if (settings.ContributionEnabled && !string.IsNullOrWhiteSpace(settings.ContributionContributorId))
                {
                    bool idChanged = !string.Equals(
                        currentSettings.ContributionContributorId?.Trim(),
                        settings.ContributionContributorId?.Trim(),
                        StringComparison.OrdinalIgnoreCase);
                    bool serverChanged = !string.Equals(
                        currentSettings.ContributionServerUrl?.Trim().TrimEnd('/'),
                        settings.ContributionServerUrl?.Trim().TrimEnd('/'),
                        StringComparison.OrdinalIgnoreCase);
                    bool wasVerified = currentSettings.ContributionVerified;

                    _logger.LogInformation("Contribution settings save: enabled={Enabled}, contributor={Contributor}, idChanged={IdChanged}, serverChanged={ServerChanged}, wasVerified={WasVerified}",
                        settings.ContributionEnabled, settings.ContributionContributorId, idChanged, serverChanged, wasVerified);

                    // Re-verify when the contributor Id or server URL changed, or when
                    // the stored flag is stale (e.g. the contributor was banned upstream).
                    if (idChanged || serverChanged || !wasVerified)
                    {
                        var verification = await _settingsService.VerifyContributorAsync(
                            settings.ContributionServerUrl,
                            settings.ContributionContributorId,
                            token).ConfigureAwait(false);

                        _logger.LogInformation("Contribution verification result: verified={Verified}, error={Error}",
                            verification.Verified, verification.Error);

                        // Persist flag + id + server URL together so a settings refetch
                        // right after verification keeps the typed Contributor Id.
                        await _settingsService.SetContributionVerifiedAsync(
                            verification.Verified,
                            token,
                            settings.ContributionContributorId,
                            settings.ContributionServerUrl).ConfigureAwait(false);

                        // Every time verification SUCCEEDS (incl. the very first enable / a flag
                        // that was stale/disabled): migrate the whole SeriesMappings graph into the
                        // contribution database so mappings participate in cloud contribution sync.
                        // Propagation is idempotent (ADD/UPDATE = Version 0, identical rows are left
                        // untouched), so re-running on every successful verify is safe and guarantees
                        // the trigger can never be missed due to caching/flag-ordering edge cases.
                        if (verification.Verified)
                        {
                            _logger.LogInformation("Contribution verified — triggering full SeriesMappings → contributor.db migration.");
                            await _contributionPropagation.SyncAllSeriesAsync(token).ConfigureAwait(false);
                        }
                        else
                        {
                            _logger.LogInformation("Contribution NOT verified — skipping full migration.");
                        }

                        if (!verification.Verified)
                        {
                            response.Message = verification.Error ?? "Contributor Id could not be verified.";
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Contribution settings unchanged (id/server same, already verified) — no re-verify needed.");
                    }
                }
                else
                {
                    // Contribution is off, or no Contributor Id — nothing can be verified.
                    // ALWAYS clear the verified flag (persisted + in-memory cache): if it only
                    // cleared when the cached value was true, disabling + re-enabling could leave a
                    // stale cached "verified=true" and the next enable+save would take the
                    // "unchanged — already verified" branch, never re-verifying nor triggering the
                    // full SeriesMappings migration. Unconditional clearing guarantees the flag
                    // reflects reality after every save.
                    await _settingsService.SetContributionVerifiedAsync(false, token).ConfigureAwait(false);
                }

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

                        string cleanDomain = Request.ResolveBaseUrl(settings.ExternalDomain);
                        response.SetPasswordUrl = $"{cleanDomain}/auth/set-password?username={Uri.EscapeDataString(user.Username)}&token={user.PasswordSetToken}";
                        response.Message = "Authentication enabled. You must set a password to log in.";
                    }
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                if (ex is ArgumentException) return BadRequest(new { error = ex.Message });
                _logger.LogError(ex, "Error updating settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while updating settings" });
            }
        }

        /// <summary>
        /// Response DTO for the contributor verification endpoint.
        /// </summary>
        public class VerifyContributorResponseDto
        {
            [JsonPropertyName("verified")]
            public bool Verified { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("banReason")]
            public string? BanReason { get; set; }

            [JsonPropertyName("isAdmin")]
            public bool IsAdmin { get; set; }

            [JsonPropertyName("contributionVerified")]
            public bool ContributionVerified { get; set; }
        }

        /// <summary>
        /// Request DTO for the contributor verification endpoint.
        /// </summary>
        public class VerifyContributorRequestDto
        {
            [JsonPropertyName("serverUrl")]
            public string ServerUrl { get; set; } = string.Empty;

            [JsonPropertyName("contributorId")]
            public string ContributorId { get; set; } = string.Empty;
        }

        /// <summary>
        /// Verifies a Contributor Id against the cloud contribution database
        /// (RensaioContributionDB.CF). On success, persists the verified flag so
        /// all contribution features are unlocked. On failure, clears it.
        /// </summary>
        /// <response code="200">Verification processed — check <c>verified</c> in the body.</response>
        /// <response code="400">Missing server URL or contributor Id.</response>
        [HttpPost("verify-contributor")]
        [RequireUserLevel(UserLevel.Owner)]
        [ProducesResponseType(typeof(VerifyContributorResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<VerifyContributorResponseDto>> VerifyContributorAsync(
            [FromBody][Required] VerifyContributorRequestDto request,
            CancellationToken token = default)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { errors = ModelState });
            }

            if (string.IsNullOrWhiteSpace(request.ServerUrl))
            {
                return BadRequest(new { error = "Contribution server URL is required." });
            }

            if (string.IsNullOrWhiteSpace(request.ContributorId))
            {
                return BadRequest(new { error = "Contributor Id is required." });
            }

            try
            {
                var result = await _settingsService.VerifyContributorAsync(
                    request.ServerUrl,
                    request.ContributorId,
                    token).ConfigureAwait(false);

                // Persist flag + id + server URL atomically so the Contributor Id
                // stays in the textbox after the frontend refetches settings.
                await _settingsService.SetContributionVerifiedAsync(
                    result.Verified,
                    token,
                    request.ContributorId,
                    request.ServerUrl).ConfigureAwait(false);

                return Ok(new VerifyContributorResponseDto
                {
                    Verified = result.Verified,
                    Error = result.Error,
                    BanReason = result.BanReason,
                    IsAdmin = result.IsAdmin,
                    ContributionVerified = result.Verified,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying contributor");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while verifying the contributor" });
            }
        }
    }
}