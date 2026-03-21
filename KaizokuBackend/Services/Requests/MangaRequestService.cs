using KaizokuBackend.Data;
using KaizokuBackend.Hubs;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto.Auth;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Series;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Requests
{
    public class MangaRequestService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<MangaRequestService> _logger;
        private readonly SeriesCommandService _seriesCommand;
        private readonly IHubContext<ProgressHub> _hubContext;

        public MangaRequestService(AppDbContext db, ILogger<MangaRequestService> logger,
            SeriesCommandService seriesCommand,
            IHubContext<ProgressHub> hubContext)
        {
            _db = db;
            _logger = logger;
            _seriesCommand = seriesCommand;
            _hubContext = hubContext;
        }

        public async Task<MangaRequestDto> CreateAsync(CreateRequestDto dto, Guid userId, CancellationToken token = default)
        {
            // Check pending request limit
            var settingsEntity = await _db.Settings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Name == "MaxPendingRequestsPerUser", token)
                .ConfigureAwait(false);

            int maxPending = 10;
            if (settingsEntity != null && int.TryParse(settingsEntity.Value, out var parsed))
                maxPending = parsed;

            var pendingCount = await _db.MangaRequests
                .CountAsync(r => r.RequestedByUserId == userId && r.Status == RequestStatus.Pending, token)
                .ConfigureAwait(false);

            if (pendingCount >= maxPending)
                throw new InvalidOperationException($"You have reached the maximum number of pending requests ({maxPending}).");

            var entity = new MangaRequestEntity
            {
                Id = Guid.NewGuid(),
                RequestedByUserId = userId,
                Title = dto.Title.Trim(),
                Description = dto.Description?.Trim(),
                ThumbnailUrl = dto.ThumbnailUrl,
                ProviderData = dto.ProviderData,
                Status = RequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _db.MangaRequests.Add(entity);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            return await MapToDtoAsync(entity, token).ConfigureAwait(false);
        }

        public async Task<MangaRequestDto> ApproveAsync(Guid requestId, Guid adminUserId, ApproveRequestDto dto, CancellationToken token = default)
        {
            var request = await _db.MangaRequests
                .FirstOrDefaultAsync(r => r.Id == requestId, token)
                .ConfigureAwait(false);

            if (request == null)
                throw new InvalidOperationException("Request not found.");

            if (request.Status != RequestStatus.Pending)
                throw new InvalidOperationException("Request is not pending.");

            request.Status = RequestStatus.Approved;
            request.ReviewedByUserId = adminUserId;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewNote = dto.ReviewNote;

            // Add series if data provided
            if (dto.SeriesData != null)
            {
                try
                {
                    await _seriesCommand.AddSeriesAsync(dto.SeriesData, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add series from approved request {RequestId}", requestId);
                }
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            // Send notification via SignalR
            try
            {
                await _hubContext.Clients.All.SendAsync("RequestUpdated", new
                {
                    requestId = request.Id,
                    status = "Approved",
                    userId = request.RequestedByUserId
                }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SignalR notification for request approval");
            }

            return await MapToDtoAsync(request, token).ConfigureAwait(false);
        }

        public async Task<MangaRequestDto> DenyAsync(Guid requestId, Guid adminUserId, DenyRequestDto dto, CancellationToken token = default)
        {
            var request = await _db.MangaRequests
                .FirstOrDefaultAsync(r => r.Id == requestId, token)
                .ConfigureAwait(false);

            if (request == null)
                throw new InvalidOperationException("Request not found.");

            if (request.Status != RequestStatus.Pending)
                throw new InvalidOperationException("Request is not pending.");

            request.Status = RequestStatus.Denied;
            request.ReviewedByUserId = adminUserId;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewNote = dto.ReviewNote;

            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            // Send notification via SignalR
            try
            {
                await _hubContext.Clients.All.SendAsync("RequestUpdated", new
                {
                    requestId = request.Id,
                    status = "Denied",
                    userId = request.RequestedByUserId
                }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SignalR notification for request denial");
            }

            return await MapToDtoAsync(request, token).ConfigureAwait(false);
        }

        public async Task<MangaRequestDto> CancelAsync(Guid requestId, Guid userId, CancellationToken token = default)
        {
            var request = await _db.MangaRequests
                .FirstOrDefaultAsync(r => r.Id == requestId, token)
                .ConfigureAwait(false);

            if (request == null)
                throw new InvalidOperationException("Request not found.");

            if (request.RequestedByUserId != userId)
                throw new InvalidOperationException("You can only cancel your own requests.");

            if (request.Status != RequestStatus.Pending)
                throw new InvalidOperationException("Only pending requests can be cancelled.");

            request.Status = RequestStatus.Cancelled;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            return await MapToDtoAsync(request, token).ConfigureAwait(false);
        }

        public async Task<List<MangaRequestDto>> GetAllAsync(CancellationToken token = default)
        {
            var requests = await _db.MangaRequests
                .AsNoTracking()
                .Include(r => r.RequestedByUser)
                .Include(r => r.ReviewedByUser)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(token)
                .ConfigureAwait(false);

            return requests.Select(MapToDtoInternal).ToList();
        }

        public async Task<List<MangaRequestDto>> GetPendingAsync(CancellationToken token = default)
        {
            var requests = await _db.MangaRequests
                .AsNoTracking()
                .Include(r => r.RequestedByUser)
                .Where(r => r.Status == RequestStatus.Pending)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(token)
                .ConfigureAwait(false);

            return requests.Select(MapToDtoInternal).ToList();
        }

        public async Task<List<MangaRequestDto>> GetByUserAsync(Guid userId, CancellationToken token = default)
        {
            var requests = await _db.MangaRequests
                .AsNoTracking()
                .Include(r => r.RequestedByUser)
                .Include(r => r.ReviewedByUser)
                .Where(r => r.RequestedByUserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(token)
                .ConfigureAwait(false);

            return requests.Select(MapToDtoInternal).ToList();
        }

        public async Task<int> GetPendingCountAsync(CancellationToken token = default)
        {
            return await _db.MangaRequests
                .CountAsync(r => r.Status == RequestStatus.Pending, token)
                .ConfigureAwait(false);
        }

        private async Task<MangaRequestDto> MapToDtoAsync(MangaRequestEntity entity, CancellationToken token)
        {
            var requestedBy = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == entity.RequestedByUserId, token)
                .ConfigureAwait(false);

            string? reviewedByUsername = null;
            if (entity.ReviewedByUserId.HasValue)
            {
                var reviewedBy = await _db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == entity.ReviewedByUserId.Value, token)
                    .ConfigureAwait(false);
                reviewedByUsername = reviewedBy?.Username;
            }

            return new MangaRequestDto
            {
                Id = entity.Id,
                RequestedByUserId = entity.RequestedByUserId,
                RequestedByUsername = requestedBy?.Username ?? "Unknown",
                Title = entity.Title,
                Description = entity.Description,
                ThumbnailUrl = entity.ThumbnailUrl,
                ProviderData = entity.ProviderData,
                Status = entity.Status,
                ReviewedByUserId = entity.ReviewedByUserId,
                ReviewedByUsername = reviewedByUsername,
                ReviewedAt = entity.ReviewedAt,
                ReviewNote = entity.ReviewNote,
                CreatedAt = entity.CreatedAt
            };
        }

        private static MangaRequestDto MapToDtoInternal(MangaRequestEntity entity)
        {
            return new MangaRequestDto
            {
                Id = entity.Id,
                RequestedByUserId = entity.RequestedByUserId,
                RequestedByUsername = entity.RequestedByUser?.Username ?? "Unknown",
                Title = entity.Title,
                Description = entity.Description,
                ThumbnailUrl = entity.ThumbnailUrl,
                ProviderData = entity.ProviderData,
                Status = entity.Status,
                ReviewedByUserId = entity.ReviewedByUserId,
                ReviewedByUsername = entity.ReviewedByUser?.Username,
                ReviewedAt = entity.ReviewedAt,
                ReviewNote = entity.ReviewNote,
                CreatedAt = entity.CreatedAt
            };
        }
    }
}
