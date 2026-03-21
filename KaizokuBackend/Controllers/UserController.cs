using KaizokuBackend.Models.Dto.Auth;
using KaizokuBackend.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Produces("application/json")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly PermissionService _permissionService;
        private readonly UserPreferencesService _preferencesService;
        private readonly ILogger<UserController> _logger;
        private readonly IConfiguration _config;

        public UserController(UserService userService, PermissionService permissionService,
            UserPreferencesService preferencesService, ILogger<UserController> logger, IConfiguration config)
        {
            _userService = userService;
            _permissionService = permissionService;
            _preferencesService = preferencesService;
            _logger = logger;
            _config = config;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var id))
                throw new InvalidOperationException("Missing or invalid UserId claim in JWT token.");
            return id;
        }

        // ─── Admin endpoints ───────────────────────────────────────────

        [HttpGet]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(List<UserDetailDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<UserDetailDto>>> GetAllUsersAsync(CancellationToken token = default)
        {
            try
            {
                var users = await _userService.GetAllAsync(token).ConfigureAwait(false);
                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users");
                return StatusCode(500, new { error = "An error occurred while retrieving users" });
            }
        }

        [HttpPost]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<UserDetailDto>> CreateUserAsync([FromBody] CreateUserDto dto, CancellationToken token = default)
        {
            try
            {
                var user = await _userService.CreateAsync(dto, token).ConfigureAwait(false);
                return Ok(user);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return StatusCode(500, new { error = "An error occurred while creating user" });
            }
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserDetailDto>> GetUserByIdAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                var user = await _userService.GetByIdAsync(id, token).ConfigureAwait(false);
                if (user == null) return NotFound(new { error = "User not found" });
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user");
                return StatusCode(500, new { error = "An error occurred while retrieving user" });
            }
        }

        [HttpPatch("{id:guid}")]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<UserDetailDto>> UpdateUserAsync([FromRoute] Guid id, [FromBody] UpdateUserDto dto, CancellationToken token = default)
        {
            try
            {
                var user = await _userService.UpdateAsync(id, dto, token).ConfigureAwait(false);
                return Ok(user);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user");
                return StatusCode(500, new { error = "An error occurred while updating user" });
            }
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> DeleteUserAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (id == currentUserId)
                    return BadRequest(new { error = "You cannot delete your own account" });

                await _userService.DeleteAsync(id, token).ConfigureAwait(false);
                return Ok(new { message = "User deleted successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user");
                return StatusCode(500, new { error = "An error occurred while deleting user" });
            }
        }

        [HttpPatch("{id:guid}/permissions")]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> UpdatePermissionsAsync([FromRoute] Guid id, [FromBody] UpdatePermissionDto dto, CancellationToken token = default)
        {
            try
            {
                await _permissionService.UpdatePermissionsAsync(id, dto, token).ConfigureAwait(false);
                return Ok(new { message = "Permissions updated successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating permissions");
                return StatusCode(500, new { error = "An error occurred while updating permissions" });
            }
        }

        [HttpPost("{id:guid}/reset-password")]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> ResetPasswordAsync([FromRoute] Guid id, [FromBody] ResetPasswordDto dto, CancellationToken token = default)
        {
            try
            {
                await _userService.ResetPasswordAsync(id, dto.NewPassword, token).ConfigureAwait(false);
                return Ok(new { message = "Password reset successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password");
                return StatusCode(500, new { error = "An error occurred while resetting password" });
            }
        }

        [HttpPost("{id:guid}/disable")]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> DisableUserAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (id == currentUserId)
                    return BadRequest(new { error = "You cannot disable your own account" });

                await _userService.DisableAsync(id, token).ConfigureAwait(false);
                return Ok(new { message = "User disabled successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling user");
                return StatusCode(500, new { error = "An error occurred while disabling user" });
            }
        }

        [HttpPost("{id:guid}/enable")]
        [Authorize(Policy = "RequireAdmin")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> EnableUserAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                await _userService.EnableAsync(id, token).ConfigureAwait(false);
                return Ok(new { message = "User enabled successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling user");
                return StatusCode(500, new { error = "An error occurred while enabling user" });
            }
        }

        // ─── Self endpoints ────────────────────────────────────────────

        [HttpGet("me")]
        [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<UserDetailDto>> GetCurrentUserAsync(CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _userService.GetByIdAsync(userId, token).ConfigureAwait(false);
                if (user == null) return NotFound(new { error = "User not found" });
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user");
                return StatusCode(500, new { error = "An error occurred while retrieving user profile" });
            }
        }

        [HttpPatch("me")]
        [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<UserDto>> UpdateProfileAsync([FromBody] UpdateUserDto dto, CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _userService.UpdateProfileAsync(userId, dto, token).ConfigureAwait(false);
                return Ok(user);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile");
                return StatusCode(500, new { error = "An error occurred while updating profile" });
            }
        }

        [HttpPatch("me/password")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> ChangePasswordAsync([FromBody] ChangePasswordDto dto, CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _userService.ChangePasswordAsync(userId, dto, token).ConfigureAwait(false);
                return Ok(new { message = "Password changed successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return StatusCode(500, new { error = "An error occurred while changing password" });
            }
        }

        [HttpGet("me/preferences")]
        [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<UserPreferencesDto>> GetPreferencesAsync(CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                var prefs = await _preferencesService.GetAsync(userId, token).ConfigureAwait(false);
                return Ok(prefs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting preferences");
                return StatusCode(500, new { error = "An error occurred while retrieving preferences" });
            }
        }

        [HttpPatch("me/preferences")]
        [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<UserPreferencesDto>> UpdatePreferencesAsync([FromBody] UpdatePreferencesDto dto, CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                var prefs = await _preferencesService.UpdateAsync(userId, dto, token).ConfigureAwait(false);
                return Ok(prefs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating preferences");
                return StatusCode(500, new { error = "An error occurred while updating preferences" });
            }
        }

        [HttpPost("me/avatar")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> UploadAvatarAsync([FromForm] IFormFile file, CancellationToken token = default)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { error = "No file uploaded" });

                if (file.Length > 5 * 1024 * 1024)
                    return BadRequest(new { error = "File size must be less than 5MB" });

                var userId = GetCurrentUserId();
                var storagePath = _config["StorageFolder"] ?? _config["runtimeDirectory"] ?? ".";
                var avatarUrl = await _userService.UploadAvatarAsync(userId, file, storagePath, token).ConfigureAwait(false);
                return Ok(new { avatarPath = avatarUrl });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading avatar");
                return StatusCode(500, new { error = "An error occurred while uploading avatar" });
            }
        }

        [HttpGet("avatar/{id:guid}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAvatarAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                var path = await _userService.GetAvatarPathAsync(id, token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                    return NotFound();

                var ext = Path.GetExtension(path).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream"
                };

                var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                return File(stream, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving avatar");
                return StatusCode(500, "An error occurred while retrieving the avatar.");
            }
        }
    }
}
