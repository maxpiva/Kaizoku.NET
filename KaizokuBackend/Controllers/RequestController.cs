using KaizokuBackend.Models.Dto.Auth;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
{
    [ApiController]
    [Route("api/requests")]
    [Produces("application/json")]
    [Authorize]
    public class RequestController : ControllerBase
    {
        private readonly MangaRequestService _requestService;
        private readonly ILogger<RequestController> _logger;

        public RequestController(MangaRequestService requestService, ILogger<RequestController> logger)
        {
            _requestService = requestService;
            _logger = logger;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var id))
                throw new InvalidOperationException("Missing or invalid UserId claim in JWT token.");
            return id;
        }

        private bool IsAdmin()
        {
            var roleClaim = User.FindFirst("Role")?.Value;
            return roleClaim != null && Enum.TryParse<UserRole>(roleClaim, out var role) && role == UserRole.Admin;
        }

        [HttpPost]
        [Authorize(Policy = "RequirePermission:CanRequestSeries")]
        [ProducesResponseType(typeof(MangaRequestDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<MangaRequestDto>> CreateRequestAsync([FromBody] CreateRequestDto dto, CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                var request = await _requestService.CreateAsync(dto, userId, token).ConfigureAwait(false);
                return Ok(request);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating request");
                return StatusCode(500, new { error = "An error occurred while creating request" });
            }
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<MangaRequestDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<MangaRequestDto>>> GetRequestsAsync(CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                List<MangaRequestDto> requests;

                if (IsAdmin())
                {
                    requests = await _requestService.GetAllAsync(token).ConfigureAwait(false);
                }
                else
                {
                    requests = await _requestService.GetByUserAsync(userId, token).ConfigureAwait(false);
                }

                return Ok(requests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting requests");
                return StatusCode(500, new { error = "An error occurred while retrieving requests" });
            }
        }

        [HttpGet("pending-count")]
        [Authorize(Policy = "RequirePermission:CanManageRequests")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetPendingCountAsync(CancellationToken token = default)
        {
            try
            {
                var count = await _requestService.GetPendingCountAsync(token).ConfigureAwait(false);
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending count");
                return StatusCode(500, new { error = "An error occurred while getting pending count" });
            }
        }

        [HttpPatch("{id:guid}/approve")]
        [Authorize(Policy = "RequirePermission:CanManageRequests")]
        [ProducesResponseType(typeof(MangaRequestDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<MangaRequestDto>> ApproveRequestAsync([FromRoute] Guid id, [FromBody] ApproveRequestDto? dto, CancellationToken token = default)
        {
            try
            {
                var adminUserId = GetCurrentUserId();
                var request = await _requestService.ApproveAsync(id, adminUserId, dto ?? new ApproveRequestDto(), token).ConfigureAwait(false);
                return Ok(request);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving request");
                return StatusCode(500, new { error = "An error occurred while approving request" });
            }
        }

        [HttpPatch("{id:guid}/deny")]
        [Authorize(Policy = "RequirePermission:CanManageRequests")]
        [ProducesResponseType(typeof(MangaRequestDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<MangaRequestDto>> DenyRequestAsync([FromRoute] Guid id, [FromBody] DenyRequestDto? dto, CancellationToken token = default)
        {
            try
            {
                var adminUserId = GetCurrentUserId();
                var request = await _requestService.DenyAsync(id, adminUserId, dto ?? new DenyRequestDto(), token).ConfigureAwait(false);
                return Ok(request);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error denying request");
                return StatusCode(500, new { error = "An error occurred while denying request" });
            }
        }

        [HttpPatch("{id:guid}/cancel")]
        [ProducesResponseType(typeof(MangaRequestDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<MangaRequestDto>> CancelRequestAsync([FromRoute] Guid id, CancellationToken token = default)
        {
            try
            {
                var userId = GetCurrentUserId();
                var request = await _requestService.CancelAsync(id, userId, token).ConfigureAwait(false);
                return Ok(request);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling request");
                return StatusCode(500, new { error = "An error occurred while cancelling request" });
            }
        }
    }
}
