using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto.Auth;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Auth
{
    public class PermissionPresetService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PermissionPresetService> _logger;

        public PermissionPresetService(AppDbContext db, ILogger<PermissionPresetService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<PermissionPresetDto> CreateAsync(CreatePresetDto dto, Guid createdByUserId, CancellationToken token = default)
        {
            var entity = new PermissionPresetEntity
            {
                Id = Guid.NewGuid(),
                Name = dto.Name.Trim(),
                CreatedByUserId = createdByUserId,
                IsDefault = false,
                CanViewLibrary = dto.Permissions.CanViewLibrary,
                CanRequestSeries = dto.Permissions.CanRequestSeries,
                CanAddSeries = dto.Permissions.CanAddSeries,
                CanEditSeries = dto.Permissions.CanEditSeries,
                CanDeleteSeries = dto.Permissions.CanDeleteSeries,
                CanManageDownloads = dto.Permissions.CanManageDownloads,
                CanViewQueue = dto.Permissions.CanViewQueue,
                CanBrowseSources = dto.Permissions.CanBrowseSources,
                CanViewNSFW = dto.Permissions.CanViewNSFW,
                CanManageRequests = dto.Permissions.CanManageRequests,
                CanManageJobs = dto.Permissions.CanManageJobs,
                CanViewStatistics = dto.Permissions.CanViewStatistics
            };

            _db.PermissionPresets.Add(entity);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            return MapToDto(entity);
        }

        public async Task<PermissionPresetDto> UpdateAsync(Guid id, UpdatePresetDto dto, CancellationToken token = default)
        {
            var entity = await _db.PermissionPresets
                .FirstOrDefaultAsync(p => p.Id == id, token)
                .ConfigureAwait(false);

            if (entity == null)
                throw new InvalidOperationException("Permission preset not found.");

            if (!string.IsNullOrWhiteSpace(dto.Name))
                entity.Name = dto.Name.Trim();

            if (dto.Permissions != null)
            {
                entity.CanViewLibrary = dto.Permissions.CanViewLibrary;
                entity.CanRequestSeries = dto.Permissions.CanRequestSeries;
                entity.CanAddSeries = dto.Permissions.CanAddSeries;
                entity.CanEditSeries = dto.Permissions.CanEditSeries;
                entity.CanDeleteSeries = dto.Permissions.CanDeleteSeries;
                entity.CanManageDownloads = dto.Permissions.CanManageDownloads;
                entity.CanViewQueue = dto.Permissions.CanViewQueue;
                entity.CanBrowseSources = dto.Permissions.CanBrowseSources;
                entity.CanViewNSFW = dto.Permissions.CanViewNSFW;
                entity.CanManageRequests = dto.Permissions.CanManageRequests;
                entity.CanManageJobs = dto.Permissions.CanManageJobs;
                entity.CanViewStatistics = dto.Permissions.CanViewStatistics;
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            return MapToDto(entity);
        }

        public async Task DeleteAsync(Guid id, CancellationToken token = default)
        {
            var entity = await _db.PermissionPresets
                .FirstOrDefaultAsync(p => p.Id == id, token)
                .ConfigureAwait(false);

            if (entity == null)
                throw new InvalidOperationException("Permission preset not found.");

            _db.PermissionPresets.Remove(entity);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task<List<PermissionPresetDto>> ListAsync(CancellationToken token = default)
        {
            var presets = await _db.PermissionPresets
                .AsNoTracking()
                .OrderBy(p => p.Name)
                .ToListAsync(token)
                .ConfigureAwait(false);

            return presets.Select(MapToDto).ToList();
        }

        public async Task<PermissionPresetDto?> GetByIdAsync(Guid id, CancellationToken token = default)
        {
            var entity = await _db.PermissionPresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, token)
                .ConfigureAwait(false);

            return entity != null ? MapToDto(entity) : null;
        }

        public async Task SetDefaultAsync(Guid id, CancellationToken token = default)
        {
            // Remove current default
            var currentDefaults = await _db.PermissionPresets
                .Where(p => p.IsDefault)
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var current in currentDefaults)
            {
                current.IsDefault = false;
            }

            var entity = await _db.PermissionPresets
                .FirstOrDefaultAsync(p => p.Id == id, token)
                .ConfigureAwait(false);

            if (entity == null)
                throw new InvalidOperationException("Permission preset not found.");

            entity.IsDefault = true;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task ClearDefaultAsync(CancellationToken token = default)
        {
            var currentDefaults = await _db.PermissionPresets
                .Where(p => p.IsDefault)
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var current in currentDefaults)
            {
                current.IsDefault = false;
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public static PermissionPresetDto MapToDto(PermissionPresetEntity entity)
        {
            return new PermissionPresetDto
            {
                Id = entity.Id,
                Name = entity.Name,
                IsDefault = entity.IsDefault,
                CreatedByUserId = entity.CreatedByUserId,
                Permissions = new PermissionDto
                {
                    CanViewLibrary = entity.CanViewLibrary,
                    CanRequestSeries = entity.CanRequestSeries,
                    CanAddSeries = entity.CanAddSeries,
                    CanEditSeries = entity.CanEditSeries,
                    CanDeleteSeries = entity.CanDeleteSeries,
                    CanManageDownloads = entity.CanManageDownloads,
                    CanViewQueue = entity.CanViewQueue,
                    CanBrowseSources = entity.CanBrowseSources,
                    CanViewNSFW = entity.CanViewNSFW,
                    CanManageRequests = entity.CanManageRequests,
                    CanManageJobs = entity.CanManageJobs,
                    CanViewStatistics = entity.CanViewStatistics
                }
            };
        }
    }
}
