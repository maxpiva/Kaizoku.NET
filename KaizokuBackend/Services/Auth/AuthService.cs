using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto.Auth;
using KaizokuBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace KaizokuBackend.Services.Auth
{
    public class AuthService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AuthService> _logger;
        private readonly InviteLinkService _inviteLinkService;
        private readonly PermissionService _permissionService;

        public AuthService(AppDbContext db, ILogger<AuthService> logger,
            InviteLinkService inviteLinkService, PermissionService permissionService)
        {
            _db = db;
            _logger = logger;
            _inviteLinkService = inviteLinkService;
            _permissionService = permissionService;
        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto, string? ipAddress, string? userAgent, CancellationToken token = default)
        {
            // Validate invite code
            var invite = await _inviteLinkService.ValidateAsync(dto.InviteCode, token).ConfigureAwait(false);
            if (invite == null)
                throw new InvalidOperationException("Invalid or expired invite code.");

            // Check for duplicate username/email
            var existingUser = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == dto.Username || u.Email == dto.Email, token)
                .ConfigureAwait(false);

            if (existingUser != null)
            {
                if (existingUser.Username == dto.Username)
                    throw new InvalidOperationException("Username is already taken.");
                throw new InvalidOperationException("Email is already in use.");
            }

            // Validate password against policy
            var passwordError = PasswordPolicy.Validate(dto.Password);
            if (passwordError != null)
                throw new InvalidOperationException(passwordError);

            // Create user
            var salt = GenerateSalt();
            var user = new UserEntity
            {
                Id = Guid.NewGuid(),
                Username = dto.Username.Trim(),
                Email = dto.Email.Trim().ToLowerInvariant(),
                DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Username : dto.DisplayName.Trim(),
                PasswordHash = HashPassword(dto.Password, salt),
                Salt = salt,
                Role = UserRole.User,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _db.Users.Add(user);

            // Create permissions from preset or defaults
            var permissions = new UserPermissionEntity { UserId = user.Id };

            if (invite.PermissionPresetId.HasValue)
            {
                var preset = await _db.PermissionPresets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == invite.PermissionPresetId.Value, token)
                    .ConfigureAwait(false);

                if (preset != null)
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
            }
            else
            {
                // Check for default preset
                var defaultPreset = await _db.PermissionPresets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IsDefault, token)
                    .ConfigureAwait(false);

                if (defaultPreset != null)
                {
                    permissions.CanViewLibrary = defaultPreset.CanViewLibrary;
                    permissions.CanRequestSeries = defaultPreset.CanRequestSeries;
                    permissions.CanAddSeries = defaultPreset.CanAddSeries;
                    permissions.CanEditSeries = defaultPreset.CanEditSeries;
                    permissions.CanDeleteSeries = defaultPreset.CanDeleteSeries;
                    permissions.CanManageDownloads = defaultPreset.CanManageDownloads;
                    permissions.CanViewQueue = defaultPreset.CanViewQueue;
                    permissions.CanBrowseSources = defaultPreset.CanBrowseSources;
                    permissions.CanViewNSFW = defaultPreset.CanViewNSFW;
                    permissions.CanManageRequests = defaultPreset.CanManageRequests;
                    permissions.CanManageJobs = defaultPreset.CanManageJobs;
                    permissions.CanViewStatistics = defaultPreset.CanViewStatistics;
                }
            }

            _db.UserPermissions.Add(permissions);

            // Create default preferences
            var preferences = new UserPreferencesEntity { UserId = user.Id };
            _db.UserPreferences.Add(preferences);

            // Save user first, then mark invite as used. This prevents the invite
            // from being consumed if user creation fails (e.g., unique constraint).
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            await _inviteLinkService.UseAsync(dto.InviteCode, token).ConfigureAwait(false);

            // Generate tokens
            return await CreateAuthResponseAsync(user, permissions, false, ipAddress, userAgent, token).ConfigureAwait(false);
        }

        public async Task<AuthResponseDto> LoginAsync(LoginDto dto, string? ipAddress, string? userAgent, CancellationToken token = default)
        {
            var usernameOrEmail = dto.UsernameOrEmail.Trim();
            var user = await _db.Users
                .Include(u => u.Permissions)
                .FirstOrDefaultAsync(u => u.Username == usernameOrEmail || u.Email == usernameOrEmail.ToLowerInvariant(), token)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException("Invalid username/email or password.");

            if (!user.IsActive)
                throw new InvalidOperationException("Account is disabled.");

            if (!VerifyPassword(dto.Password, user.PasswordHash, user.Salt))
                throw new InvalidOperationException("Invalid username/email or password.");

            // Check if the user's password meets the current policy (detects legacy weak passwords)
            bool requiresPasswordChange = !PasswordPolicy.MeetsPolicy(dto.Password);

            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            var permissions = user.Permissions ?? new UserPermissionEntity { UserId = user.Id };
            var response = await CreateAuthResponseAsync(user, permissions, dto.RememberMe, ipAddress, userAgent, token).ConfigureAwait(false);
            response.RequiresPasswordChange = requiresPasswordChange;
            return response;
        }

        public async Task LogoutAsync(Guid userId, string refreshToken, CancellationToken token = default)
        {
            var session = await _db.UserSessions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.RefreshToken == refreshToken && !s.IsRevoked, token)
                .ConfigureAwait(false);

            if (session != null)
            {
                session.IsRevoked = true;
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }
        }

        public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string? ipAddress, string? userAgent, CancellationToken token = default)
        {
            var session = await _db.UserSessions
                .Include(s => s.User)
                    .ThenInclude(u => u!.Permissions)
                .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken && !s.IsRevoked, token)
                .ConfigureAwait(false);

            if (session == null)
                throw new InvalidOperationException("Invalid refresh token.");

            if (session.ExpiresAt < DateTime.UtcNow)
            {
                session.IsRevoked = true;
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                throw new InvalidOperationException("Refresh token has expired.");
            }

            if (session.User == null || !session.User.IsActive)
                throw new InvalidOperationException("Account is disabled.");

            // Revoke old session
            session.IsRevoked = true;

            var user = session.User;
            var permissions = user.Permissions ?? new UserPermissionEntity { UserId = user.Id };

            // Determine if this was a remember-me session (> 2 days means it was remember-me)
            bool rememberMe = (session.ExpiresAt - session.CreatedAt).TotalDays > 2;

            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            return await CreateAuthResponseAsync(user, permissions, rememberMe, ipAddress, userAgent, token).ConfigureAwait(false);
        }

        private async Task<AuthResponseDto> CreateAuthResponseAsync(UserEntity user, UserPermissionEntity permissions,
            bool rememberMe, string? ipAddress, string? userAgent, CancellationToken token)
        {
            var jwtSecret = await GetOrCreateJwtSecretAsync(token).ConfigureAwait(false);
            var accessTokenExpiry = DateTime.UtcNow.AddMinutes(15);

            var claims = new List<Claim>
            {
                new Claim("UserId", user.Id.ToString()),
                new Claim("Username", user.Username),
                new Claim("Email", user.Email),
                new Claim("Role", user.Role.ToString()),
                new Claim("CanViewLibrary", permissions.CanViewLibrary.ToString()),
                new Claim("CanRequestSeries", permissions.CanRequestSeries.ToString()),
                new Claim("CanAddSeries", permissions.CanAddSeries.ToString()),
                new Claim("CanEditSeries", permissions.CanEditSeries.ToString()),
                new Claim("CanDeleteSeries", permissions.CanDeleteSeries.ToString()),
                new Claim("CanManageDownloads", permissions.CanManageDownloads.ToString()),
                new Claim("CanViewQueue", permissions.CanViewQueue.ToString()),
                new Claim("CanBrowseSources", permissions.CanBrowseSources.ToString()),
                new Claim("CanViewNSFW", permissions.CanViewNSFW.ToString()),
                new Claim("CanManageRequests", permissions.CanManageRequests.ToString()),
                new Claim("CanManageJobs", permissions.CanManageJobs.ToString()),
                new Claim("CanViewStatistics", permissions.CanViewStatistics.ToString()),
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var jwtToken = new JwtSecurityToken(
                issuer: "KaizokuNET",
                audience: "KaizokuNET",
                claims: claims,
                expires: accessTokenExpiry,
                signingCredentials: creds
            );

            var accessToken = new JwtSecurityTokenHandler().WriteToken(jwtToken);

            // Create refresh token
            var refreshTokenValue = GenerateRefreshToken();
            var refreshExpiry = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(1);

            var session = new UserSessionEntity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                RefreshToken = refreshTokenValue,
                ExpiresAt = refreshExpiry,
                CreatedAt = DateTime.UtcNow,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                IsRevoked = false
            };

            _db.UserSessions.Add(session);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            // Load preferences for the response
            var preferences = await _db.UserPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id, token)
                .ConfigureAwait(false);

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenValue,
                ExpiresAt = accessTokenExpiry,
                User = MapUserDetailDto(user, permissions, preferences)
            };
        }

        public async Task<string> GetOrCreateJwtSecretAsync(CancellationToken token = default)
        {
            var setting = await _db.Settings
                .FirstOrDefaultAsync(s => s.Name == "JwtSecret", token)
                .ConfigureAwait(false);

            if (setting != null)
                return setting.Value;

            // Generate a new 256-bit key
            var keyBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyBytes);
            }
            var secret = Convert.ToBase64String(keyBytes);

            _db.Settings.Add(new SettingEntity { Name = "JwtSecret", Value = secret });
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            return secret;
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

        private static string GenerateRefreshToken()
        {
            var tokenBytes = new byte[64];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(tokenBytes);
            }
            return Convert.ToBase64String(tokenBytes);
        }

        public static UserDto MapUserDto(UserEntity user)
        {
            return new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role,
                AvatarPath = user.AvatarPath,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsActive = user.IsActive
            };
        }

        public static UserDetailDto MapUserDetailDto(UserEntity user, UserPermissionEntity permissions, UserPreferencesEntity? preferences)
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
                Permissions = PermissionService.MapToDto(permissions),
                Preferences = preferences != null ? new UserPreferencesDto
                {
                    Theme = preferences.Theme,
                    DefaultLanguage = preferences.DefaultLanguage,
                    CardSize = preferences.CardSize,
                    NsfwVisibility = preferences.NsfwVisibility
                } : new UserPreferencesDto()
            };
        }
    }
}
