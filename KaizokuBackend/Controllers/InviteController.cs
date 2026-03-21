using KaizokuBackend.Models.Dto.Auth;
using KaizokuBackend.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/invites")]
    [Produces("application/json")]
    [Authorize(Policy = "RequireAdmin")]
    public class InviteController : ControllerBase
    {
        private readonly InviteLinkService _inviteService;
        private readonly ILogger<InviteController> _logger;

        public InviteController(InviteLinkService inviteService, ILogger<InviteController> logger)
        {
            _inviteService = inviteService;
            _logger = logger;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var id))
                throw new InvalidOperationException("Missing or invalid UserId claim in JWT token.");
            return id;
        }

        [HttpPost]
        [ProducesResponseType(typeof(InviteLinkDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<InviteLinkDto>> CreateInviteAsync([FromBody] CreateInviteDto dto, CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                var invite = await _inviteService.CreateAsync(dto, userId, token).ConfigureAwait(false);
                return Ok(invite);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating invite");
                return StatusCode(500, new { error = "An error occurred while creating invite" });
            }
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<InviteLinkDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<InviteLinkDto>>> GetInvitesAsync(CancellationToken token = default)
        {
            try
            {
                var invites = await _inviteService.ListActiveAsync(token).ConfigureAwait(false);
                return Ok(invites);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting invites");
                return StatusCode(500, new { error = "An error occurred while retrieving invites" });
            }
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> RevokeInviteAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                await _inviteService.RevokeAsync(id, token).ConfigureAwait(false);
                return Ok(new { message = "Invite revoked successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking invite");
                return StatusCode(500, new { error = "An error occurred while revoking invite" });
            }
        }

        [HttpGet("validate/{code}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(InviteValidationDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<InviteValidationDto>> ValidateInviteAsync([FromRoute] string code, CancellationToken token = default)
        {
            try
            {
                var result = await _inviteService.ValidateCodePublicAsync(code, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating invite");
                return StatusCode(500, new { error = "An error occurred while validating invite" });
            }
        }
    }
}
