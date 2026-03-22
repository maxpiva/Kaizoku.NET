using KaizokuBackend.Authorization;
using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto.Auth;
using KaizokuBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace KaizokuBackend.Services.Auth
{
    public class UserService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<UserService> _logger;

        public UserService(AppDbContext db, ILogger<UserService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<UserDetailDto>> GetAllAsync(CancellationToken token = default)
        {
            var users = await _db.Users
                .AsNoTracking()
                .Include(u => u.Permissions)
                .OrderBy(u => u.Username)
                .ToListAsync(token)
                .ConfigureAwait(false);

            return users.Select(MapToDetailDto).ToList();
        }

        public async Task<UserDetailDto?> GetByIdAsync(Guid id, CancellationToken token = default)
        {
            var user = await _db.Users
                .AsNoTracking()
                .Include(u => u.Permissions)
                .Include(u => u.Preferences)
                .FirstOrDefaultAsync(u => u.Id == id, token)
                .ConfigureAwait(false);

            return user != null ? MapToDetailDto(user) : null;
        }

        public async Task<UserDto?> GetByUsernameAsync(string username, CancellationToken token = default)
        {
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username, token)
                .ConfigureAwait(false);

            return user != null ? AuthService.MapUserDto(user) : null;
        }

        public async Task<UserDto?> GetByEmailAsync(string email, CancellationToken token = default)
        {
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), token)
                .ConfigureAwait(false);

            return user != null ? AuthService.MapUserDto(user) : null;
        }

        public async Task<UserDetailDto> CreateAsync(CreateUserDto dto, CancellationToken token = default)
        {
            var existing = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == dto.Username || u.Email == dto.Email.ToLowerInvariant(), token)
                .ConfigureAwait(false);

            if (existing != null)
            {
                if (existing.Username == dto.Username)
                    throw new InvalidOperationException("Username is already taken.");
                throw new InvalidOperationException("Email is already in use.");
            }

            var passwordError = PasswordPolicy.Validate(dto.Password);
            if (passwordError != null)
                throw new InvalidOperationException(passwordError);

            var salt = GenerateSalt();
            var user = new UserEntity
            {
                Id = Guid.NewGuid(),
                Username = dto.Username.Trim(),
                Email = dto.Email.Trim().ToLowerInvariant(),
                DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Username : dto.DisplayName.Trim(),
                PasswordHash = HashPassword(dto.Password, salt),
                Salt = salt,
                Role = dto.Role,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _db.Users.Add(user);

            var permissions = new UserPermissionEntity { UserId = user.Id };
            if (dto.Permissions != null)
            {
                permissions.CanViewLibrary = dto.Permissions.CanViewLibrary;
                permissions.CanRequestSeries = dto.Permissions.CanRequestSeries;
                permissions.CanAddSeries = dto.Permissions.CanAddSeries;
                permissions.CanEditSeries = dto.Permissions.CanEditSeries;
                permissions.CanDeleteSeries = dto.Permissions.CanDeleteSeries;
                permissions.CanManageDownloads = dto.Permissions.CanManageDownloads;
                permissions.CanViewQueue = dto.Permissions.CanViewQueue;
                permissions.CanBrowseSources = dto.Permissions.CanBrowseSources;
                permissions.CanViewNSFW = dto.Permissions.CanViewNSFW;
                permissions.CanManageRequests = dto.Permissions.CanManageRequests;
                permissions.CanManageJobs = dto.Permissions.CanManageJobs;
                permissions.CanViewStatistics = dto.Permissions.CanViewStatistics;
            }

            // Admin users get all permissions
            if (dto.Role == UserRole.Admin)
            {
                permissions.CanViewLibrary = true;
                permissions.CanRequestSeries = true;
                permissions.CanAddSeries = true;
                permissions.CanEditSeries = true;
                permissions.CanDeleteSeries = true;
                permissions.CanManageDownloads = true;
                permissions.CanViewQueue = true;
                permissions.CanBrowseSources = true;
                permissions.CanViewNSFW = true;
                permissions.CanManageRequests = true;
                permissions.CanManageJobs = true;
                permissions.CanViewStatistics = true;
            }

            _db.UserPermissions.Add(permissions);

            var preferences = new UserPreferencesEntity { UserId = user.Id };
            _db.UserPreferences.Add(preferences);

            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            // Invalidate bootstrap mode cache since a user now exists
            BootstrapModeMiddleware.InvalidateCache();

            user.Permissions = permissions;
            return MapToDetailDto(user);
        }

        public async Task<UserDetailDto> UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken token = default)
        {
            var user = await _db.Users
                .Include(u => u.Permissions)
                .FirstOrDefaultAsync(u => u.Id == id, token)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException("User not found.");

            if (dto.DisplayName != null)
                user.DisplayName = dto.DisplayName.Trim();

            if (dto.Email != null)
            {
                var emailLower = dto.Email.Trim().ToLowerInvariant();
                var emailExists = await _db.Users.AnyAsync(u => u.Email == emailLower && u.Id != id, token).ConfigureAwait(false);
                if (emailExists)
                    throw new InvalidOperationException("Email is already in use.");
                user.Email = emailLower;
            }

            if (dto.Role.HasValue)
                user.Role = dto.Role.Value;

            if (dto.IsActive.HasValue)
                user.IsActive = dto.IsActive.Value;

            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            return MapToDetailDto(user);
        }

        public async Task DeleteAsync(Guid id, CancellationToken token = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, token).ConfigureAwait(false);
            if (user == null)
                throw new InvalidOperationException("User not found.");

            // Delete all related data
            var sessions = await _db.UserSessions.Where(s => s.UserId == id).ToListAsync(token).ConfigureAwait(false);
            _db.UserSessions.RemoveRange(sessions);

            var permissions = await _db.UserPermissions.FirstOrDefaultAsync(p => p.UserId == id, token).ConfigureAwait(false);
            if (permissions != null) _db.UserPermissions.Remove(permissions);

            var preferences = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == id, token).ConfigureAwait(false);
            if (preferences != null) _db.UserPreferences.Remove(preferences);

            var requests = await _db.MangaRequests.Where(r => r.RequestedByUserId == id).ToListAsync(token).ConfigureAwait(false);
            _db.MangaRequests.RemoveRange(requests);

            _db.Users.Remove(user);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            // Invalidate bootstrap cache so the system can re-enter setup mode if all users are deleted
            BootstrapModeMiddleware.InvalidateCache();
        }

        public async Task DisableAsync(Guid id, CancellationToken token = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, token).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException("User not found.");

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;

            // Revoke all sessions
            var sessions = await _db.UserSessions.Where(s => s.UserId == id && !s.IsRevoked).ToListAsync(token).ConfigureAwait(false);
            foreach (var session in sessions)
                session.IsRevoked = true;

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task EnableAsync(Guid id, CancellationToken token = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, token).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException("User not found.");

            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task ResetPasswordAsync(Guid id, string newPassword, CancellationToken token = default)
        {
            var passwordError = PasswordPolicy.Validate(newPassword);
            if (passwordError != null)
                throw new InvalidOperationException(passwordError);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, token).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException("User not found.");

            var salt = GenerateSalt();
            user.PasswordHash = HashPassword(newPassword, salt);
            user.Salt = salt;
            user.UpdatedAt = DateTime.UtcNow;

            // Revoke all sessions
            var sessions = await _db.UserSessions.Where(s => s.UserId == id && !s.IsRevoked).ToListAsync(token).ConfigureAwait(false);
            foreach (var session in sessions)
                session.IsRevoked = true;

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateUserDto dto, CancellationToken token = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, token).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException("User not found.");

            if (dto.DisplayName != null)
                user.DisplayName = dto.DisplayName.Trim();

            if (dto.Email != null)
            {
                var emailLower = dto.Email.Trim().ToLowerInvariant();
                var emailExists = await _db.Users.AnyAsync(u => u.Email == emailLower && u.Id != userId, token).ConfigureAwait(false);
                if (emailExists) throw new InvalidOperationException("Email is already in use.");
                user.Email = emailLower;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            return AuthService.MapUserDto(user);
        }

        public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto, CancellationToken token = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, token).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException("User not found.");

            if (!VerifyPassword(dto.CurrentPassword, user.PasswordHash, user.Salt))
                throw new InvalidOperationException("Current password is incorrect.");

            var passwordError = PasswordPolicy.Validate(dto.NewPassword);
            if (passwordError != null)
                throw new InvalidOperationException(passwordError);

            var salt = GenerateSalt();
            user.PasswordHash = HashPassword(dto.NewPassword, salt);
            user.Salt = salt;
            user.UpdatedAt = DateTime.UtcNow;

            // Revoke all existing sessions so stolen tokens can't be reused
            var sessions = await _db.UserSessions.Where(s => s.UserId == userId && !s.IsRevoked).ToListAsync(token).ConfigureAwait(false);
            foreach (var session in sessions)
                session.IsRevoked = true;

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp"
        };

        private static readonly HashSet<string> AllowedAvatarContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/png", "image/jpeg", "image/gif", "image/webp"
        };

        public async Task<string?> UploadAvatarAsync(Guid userId, IFormFile file, string avatarStoragePath, CancellationToken token = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, token).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException("User not found.");

            // Validate both extension and content type to prevent malicious uploads
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedAvatarExtensions.Contains(ext))
                throw new InvalidOperationException("Invalid file type. Allowed: PNG, JPG, GIF, WEBP.");

            if (!string.IsNullOrEmpty(file.ContentType) && !AllowedAvatarContentTypes.Contains(file.ContentType))
                throw new InvalidOperationException("Invalid content type. Only image files are allowed.");

            // Delete old avatar if exists
            if (!string.IsNullOrEmpty(user.AvatarPath) && File.Exists(user.AvatarPath))
            {
                File.Delete(user.AvatarPath);
            }

            var fileName = $"{userId}{ext}";
            var directory = Path.Combine(avatarStoragePath, "avatars");
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream, token).ConfigureAwait(false);
            }

            user.AvatarPath = filePath;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            return $"/api/users/avatar/{userId}";
        }

        public async Task DeleteAvatarAsync(Guid userId, CancellationToken token = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, token).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException("User not found.");

            if (!string.IsNullOrEmpty(user.AvatarPath) && File.Exists(user.AvatarPath))
            {
                File.Delete(user.AvatarPath);
            }

            user.AvatarPath = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        public async Task<string?> GetAvatarPathAsync(Guid userId, CancellationToken token = default)
        {
            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, token)
                .ConfigureAwait(false);
            return user?.AvatarPath;
        }

        public async Task<bool> AnyUsersExistAsync(CancellationToken token = default)
        {
            return await _db.Users.AnyAsync(token).ConfigureAwait(false);
        }

        private static string GenerateSalt()
        {
            var saltBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(saltBytes);
            }
            return Convert.ToBase64String(saltBytes);
        }

        private static string HashPassword(string password, string salt)
        {
            return BCrypt.Net.BCrypt.HashPassword(password + salt, workFactor: 12);
        }

        private static bool VerifyPassword(string password, string hash, string salt)
        {
            return BCrypt.Net.BCrypt.Verify(password + salt, hash);
        }

        private static UserDetailDto MapToDetailDto(UserEntity user)
        {
            return new UserDetailDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role,
                AvatarPath = user.AvatarPath,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                LastLoginAt = user.LastLoginAt,
                IsActive = user.IsActive,
                Permissions = user.Permissions != null ? PermissionService.MapToDto(user.Permissions) : new PermissionDto(),
                Preferences = user.Preferences != null ? new UserPreferencesDto
                {
                    Theme = user.Preferences.Theme,
                    DefaultLanguage = user.Preferences.DefaultLanguage,
                    CardSize = user.Preferences.CardSize,
                    NsfwVisibility = user.Preferences.NsfwVisibility
                } : new UserPreferencesDto()
            };
        }
    }
}
