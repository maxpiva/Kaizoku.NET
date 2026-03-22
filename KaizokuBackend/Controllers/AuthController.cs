using KaizokuBackend.Models.Dto.Auth;
using KaizokuBackend.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;
        private readonly UserService _userService;
        private readonly ILogger<AuthController> _logger;
        private static readonly SemaphoreSlim _setupLock = new(1, 1);

        public AuthController(AuthService authService, UserService userService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _userService = userService;
            _logger = logger;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AuthResponseDto>> LoginAsync([FromBody] LoginDto dto, CancellationToken token = default)
        {
            try
            {
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers.UserAgent.ToString();
                var result = await _authService.LoginAsync(dto, ipAddress, userAgent, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return StatusCode(500, new { error = "An error occurred during login" });
            }
        }

        [HttpPost("register")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AuthResponseDto>> RegisterAsync([FromBody] RegisterDto dto, CancellationToken token = default)
        {
            try
            {
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers.UserAgent.ToString();
                var result = await _authService.RegisterAsync(dto, ipAddress, userAgent, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return StatusCode(500, new { error = "An error occurred during registration" });
            }
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AuthResponseDto>> RefreshTokenAsync([FromBody] RefreshTokenDto dto, CancellationToken token = default)
        {
            try
            {
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers.UserAgent.ToString();
                var result = await _authService.RefreshTokenAsync(dto.RefreshToken, ipAddress, userAgent, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return StatusCode(500, new { error = "An error occurred during token refresh" });
            }
        }

        [HttpPost("logout")]
        [Authorize]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> LogoutAsync([FromBody] RefreshTokenDto dto, CancellationToken token = default)
        {
            try
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    return BadRequest(new { error = "Invalid user context" });

                await _authService.LogoutAsync(userId, dto.RefreshToken, token).ConfigureAwait(false);
                return Ok(new { message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return StatusCode(500, new { error = "An error occurred during logout" });
            }
        }

        [HttpGet("status")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetStatusAsync(CancellationToken token = default)
        {
            try
            {
                var hasUsers = await _userService.AnyUsersExistAsync(token).ConfigureAwait(false);
                return Ok(new { requiresSetup = !hasUsers });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking auth status");
                return StatusCode(500, new { error = "An error occurred checking auth status" });
            }
        }

        [HttpPost("setup")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AuthResponseDto>> SetupAdminAsync([FromBody] CreateUserDto dto, CancellationToken token = default)
        {
            // Prevent TOCTOU race: two concurrent requests could both pass AnyUsersExistAsync
            // and create duplicate admin accounts. Serialize setup with a lock.
            await _setupLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var hasUsers = await _userService.AnyUsersExistAsync(token).ConfigureAwait(false);
                if (hasUsers)
                    return BadRequest(new { error = "Setup has already been completed. An admin user already exists." });

                dto.Role = Models.Enums.UserRole.Admin;
                var user = await _userService.CreateAsync(dto, token).ConfigureAwait(false);

                // Login the newly created admin
                var loginDto = new LoginDto
                {
                    UsernameOrEmail = dto.Username,
                    Password = dto.Password,
                    RememberMe = true
                };
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers.UserAgent.ToString();
                var result = await _authService.LoginAsync(loginDto, ipAddress, userAgent, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during admin setup");
                return StatusCode(500, new { error = "An error occurred during admin setup" });
            }
            finally
            {
                _setupLock.Release();
            }
        }
    }
}
