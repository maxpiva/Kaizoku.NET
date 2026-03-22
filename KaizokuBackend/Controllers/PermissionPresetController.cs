using KaizokuBackend.Models.Dto.Auth;
using KaizokuBackend.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/permission-presets")]
    [Produces("application/json")]
    [Authorize(Policy = "RequireAdmin")]
    public class PermissionPresetController : ControllerBase
    {
        private readonly PermissionPresetService _presetService;
        private readonly ILogger<PermissionPresetController> _logger;

        public PermissionPresetController(PermissionPresetService presetService, ILogger<PermissionPresetController> logger)
        {
            _presetService = presetService;
            _logger = logger;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var id))
                throw new InvalidOperationException("Missing or invalid UserId claim in JWT token.");
            return id;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<PermissionPresetDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<PermissionPresetDto>>> GetPresetsAsync(CancellationToken token = default)
        {
            try
            {
                var presets = await _presetService.ListAsync(token).ConfigureAwait(false);
                return Ok(presets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting presets");
                return StatusCode(500, new { error = "An error occurred while retrieving presets" });
            }
        }

        [HttpPost]
        [ProducesResponseType(typeof(PermissionPresetDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PermissionPresetDto>> CreatePresetAsync([FromBody] CreatePresetDto dto, CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                var preset = await _presetService.CreateAsync(dto, userId, token).ConfigureAwait(false);
                return Ok(preset);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating preset");
                return StatusCode(500, new { error = "An error occurred while creating preset" });
            }
        }

        [HttpPatch("{id:guid}")]
        [ProducesResponseType(typeof(PermissionPresetDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PermissionPresetDto>> UpdatePresetAsync([FromRoute] Guid id, [FromBody] UpdatePresetDto dto, CancellationToken token = default)
        {
            try
            {
                var preset = await _presetService.UpdateAsync(id, dto, token).ConfigureAwait(false);
                return Ok(preset);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating preset");
                return StatusCode(500, new { error = "An error occurred while updating preset" });
            }
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> DeletePresetAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                await _presetService.DeleteAsync(id, token).ConfigureAwait(false);
                return Ok(new { message = "Preset deleted successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting preset");
                return StatusCode(500, new { error = "An error occurred while deleting preset" });
            }
        }

        [HttpPost("{id:guid}/set-default")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> SetDefaultAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                await _presetService.SetDefaultAsync(id, token).ConfigureAwait(false);
                return Ok(new { message = "Default preset updated successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting default preset");
                return StatusCode(500, new { error = "An error occurred while setting default preset" });
            }
        }

        [HttpPost("clear-default")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<ActionResult> ClearDefaultAsync(CancellationToken token = default)
        {
            try
            {
                await _presetService.ClearDefaultAsync(token).ConfigureAwait(false);
                return Ok(new { message = "Default preset cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing default preset");
                return StatusCode(500, new { error = "An error occurred while clearing default preset" });
            }
        }
    }
}
