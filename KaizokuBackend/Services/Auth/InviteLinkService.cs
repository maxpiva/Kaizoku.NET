using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto.Auth;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace KaizokuBackend.Services.Auth
{
    public class InviteLinkService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<InviteLinkService> _logger;
        private static readonly char[] AlphanumericChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789".ToCharArray();

        public InviteLinkService(AppDbContext db, ILogger<InviteLinkService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<InviteLinkDto> CreateAsync(CreateInviteDto dto, Guid createdByUserId, CancellationToken token = default)
        {
            var code = GenerateCode(8);

            var entity = new InviteLinkEntity
            {
                Id = Guid.NewGuid(),
                Code = code,
                CreatedByUserId = createdByUserId,
                ExpiresAt = DateTime.UtcNow.AddDays(dto.ExpiresInDays > 0 ? dto.ExpiresInDays : 7),
                MaxUses = dto.MaxUses > 0 ? dto.MaxUses : 0,
                UsedCount = 0,
                PermissionPresetId = dto.PermissionPresetId,
                IsActive = true
            };

            _db.InviteLinks.Add(entity);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            return await MapToDtoAsync(entity, token).ConfigureAwait(false);
        }

        public async Task<InviteLinkEntity?> ValidateAsync(string code, CancellationToken token = default)
        {
            var invite = await _db.InviteLinks
                .FirstOrDefaultAsync(i => i.Code == code && i.IsActive, token)
                .ConfigureAwait(false);

            if (invite == null)
                return null;

            if (invite.ExpiresAt < DateTime.UtcNow)
                return null;

            // MaxUses == 0 means unlimited
            if (invite.MaxUses > 0 && invite.UsedCount >= invite.MaxUses)
                return null;

            return invite;
        }

        public async Task UseAsync(string code, CancellationToken token = default)
        {
            var invite = await _db.InviteLinks
                .FirstOrDefaultAsync(i => i.Code == code && i.IsActive, token)
                .ConfigureAwait(false);

            if (invite == null)
                return;

            invite.UsedCount++;

            // MaxUses == 0 means unlimited; only deactivate when a finite limit is reached
            if (invite.MaxUses > 0 && invite.UsedCount >= invite.MaxUses)
            {
                invite.IsActive = false;
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task RevokeAsync(Guid id, CancellationToken token = default)
        {
            var invite = await _db.InviteLinks
                .FirstOrDefaultAsync(i => i.Id == id, token)
                .ConfigureAwait(false);

            if (invite == null)
                throw new InvalidOperationException("Invite link not found.");

            invite.IsActive = false;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task<List<InviteLinkDto>> ListActiveAsync(CancellationToken token = default)
        {
            var invites = await _db.InviteLinks
                .Include(i => i.CreatedByUser)
                .Include(i => i.PermissionPreset)
                .Where(i => i.IsActive)
                .OrderByDescending(i => i.ExpiresAt)
                .ToListAsync(token)
                .ConfigureAwait(false);

            return invites.Select(i => MapToDtoInternal(i)).ToList();
        }

        public async Task<InviteValidationDto> ValidateCodePublicAsync(string code, CancellationToken token = default)
        {
            var invite = await _db.InviteLinks
                .Include(i => i.PermissionPreset)
                .FirstOrDefaultAsync(i => i.Code == code, token)
                .ConfigureAwait(false);

            if (invite == null)
                return new InviteValidationDto { IsValid = false, Reason = "Invite code not found." };

            if (!invite.IsActive)
                return new InviteValidationDto { IsValid = false, Reason = "This invite has been revoked." };

            if (invite.ExpiresAt < DateTime.UtcNow)
                return new InviteValidationDto { IsValid = false, Reason = "This invite has expired." };

            if (invite.MaxUses > 0 && invite.UsedCount >= invite.MaxUses)
                return new InviteValidationDto { IsValid = false, Reason = "This invite has reached its maximum uses." };

            return new InviteValidationDto
            {
                IsValid = true,
                PermissionPresetName = invite.PermissionPreset?.Name
            };
        }

        private static string GenerateCode(int length)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = AlphanumericChars[bytes[i] % AlphanumericChars.Length];
            }

            return new string(chars);
        }

        private async Task<InviteLinkDto> MapToDtoAsync(InviteLinkEntity entity, CancellationToken token)
        {
            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == entity.CreatedByUserId, token)
                .ConfigureAwait(false);

            string? presetName = null;
            if (entity.PermissionPresetId.HasValue)
            {
                var preset = await _db.PermissionPresets.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == entity.PermissionPresetId.Value, token)
                    .ConfigureAwait(false);
                presetName = preset?.Name;
            }

            return new InviteLinkDto
            {
                Id = entity.Id,
                Code = entity.Code,
                CreatedByUserId = entity.CreatedByUserId,
                CreatedByUsername = user?.Username ?? "Unknown",
                ExpiresAt = entity.ExpiresAt,
                MaxUses = entity.MaxUses,
                UsedCount = entity.UsedCount,
                PermissionPresetId = entity.PermissionPresetId,
                PermissionPresetName = presetName,
                IsActive = entity.IsActive
            };
        }

        private static InviteLinkDto MapToDtoInternal(InviteLinkEntity entity)
        {
            return new InviteLinkDto
            {
                Id = entity.Id,
                Code = entity.Code,
                CreatedByUserId = entity.CreatedByUserId,
                CreatedByUsername = entity.CreatedByUser?.Username ?? "Unknown",
                ExpiresAt = entity.ExpiresAt,
                MaxUses = entity.MaxUses,
                UsedCount = entity.UsedCount,
                PermissionPresetId = entity.PermissionPresetId,
                PermissionPresetName = entity.PermissionPreset?.Name,
                IsActive = entity.IsActive
            };
        }
    }
}
