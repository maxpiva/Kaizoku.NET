using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto.Auth;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Auth
{
    public class PermissionService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PermissionService> _logger;

        public PermissionService(AppDbContext db, ILogger<PermissionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<PermissionDto> GetPermissionsAsync(Guid userId, CancellationToken token = default)
        {
            var permissions = await _db.UserPermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, token)
                .ConfigureAwait(false);

            if (permissions == null)
                throw new InvalidOperationException("User permissions not found.");

            return MapToDto(permissions);
        }

        public async Task UpdatePermissionsAsync(Guid userId, UpdatePermissionDto dto, CancellationToken token = default)
        {
            var permissions = await _db.UserPermissions
                .FirstOrDefaultAsync(p => p.UserId == userId, token)
                .ConfigureAwait(false);

            if (permissions == null)
                throw new InvalidOperationException("User permissions not found.");

            permissions.CanViewLibrary = dto.CanViewLibrary;
            permissions.CanRequestSeries = dto.CanRequestSeries;
            permissions.CanAddSeries = dto.CanAddSeries;
            permissions.CanEditSeries = dto.CanEditSeries;
            permissions.CanDeleteSeries = dto.CanDeleteSeries;
            permissions.CanManageDownloads = dto.CanManageDownloads;
            permissions.CanViewQueue = dto.CanViewQueue;
            permissions.CanBrowseSources = dto.CanBrowseSources;
            permissions.CanViewNSFW = dto.CanViewNSFW;
            permissions.CanManageRequests = dto.CanManageRequests;
            permissions.CanManageJobs = dto.CanManageJobs;
            permissions.CanViewStatistics = dto.CanViewStatistics;

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task ApplyPresetAsync(Guid userId, Guid presetId, CancellationToken token = default)
        {
            var preset = await _db.PermissionPresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == presetId, token)
                .ConfigureAwait(false);

            if (preset == null)
                throw new InvalidOperationException("Permission preset not found.");

            var permissions = await _db.UserPermissions
                .FirstOrDefaultAsync(p => p.UserId == userId, token)
                .ConfigureAwait(false);

            if (permissions == null)
                throw new InvalidOperationException("User permissions not found.");

            ApplyPresetToPermission(preset, permissions);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task BulkApplyPresetAsync(Guid[] userIds, Guid presetId, CancellationToken token = default)
        {
            var preset = await _db.PermissionPresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == presetId, token)
                .ConfigureAwait(false);

            if (preset == null)
                throw new InvalidOperationException("Permission preset not found.");

            var permissions = await _db.UserPermissions
                .Where(p => userIds.Contains(p.UserId))
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var perm in permissions)
            {
                ApplyPresetToPermission(preset, perm);
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task<PermissionPresetDto?> GetDefaultPresetAsync(CancellationToken token = default)
        {
            var preset = await _db.PermissionPresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IsDefault, token)
                .ConfigureAwait(false);

            if (preset == null)
                return null;

            return PermissionPresetService.MapToDto(preset);
        }

        private static void ApplyPresetToPermission(PermissionPresetEntity preset, UserPermissionEntity permissions)
        {
            permissions.CanViewLibrary = preset.CanViewLibrary;
            permissions.CanRequestSeries = preset.CanRequestSeries;
            permissions.CanAddSeries = preset.CanAddSeries;
            permissions.CanEditSeries = preset.CanEditSeries;
            permissions.CanDeleteSeries = preset.CanDeleteSeries;
            permissions.CanManageDownloads = preset.CanManageDownloads;
            permissions.CanViewQueue = preset.CanViewQueue;
            permissions.CanBrowseSources = preset.CanBrowseSources;
            permissions.CanViewNSFW = preset.CanViewNSFW;
            permissions.CanManageRequests = preset.CanManageRequests;
            permissions.CanManageJobs = preset.CanManageJobs;
            permissions.CanViewStatistics = preset.CanViewStatistics;
        }

        public static PermissionDto MapToDto(UserPermissionEntity entity)
        {
            return new PermissionDto
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
            };
        }
    }
}
