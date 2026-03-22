using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto.Auth;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Auth
{
    public class UserPreferencesService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<UserPreferencesService> _logger;

        public UserPreferencesService(AppDbContext db, ILogger<UserPreferencesService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<UserPreferencesDto> GetAsync(Guid userId, CancellationToken token = default)
        {
            var prefs = await _db.UserPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, token)
                .ConfigureAwait(false);

            if (prefs == null)
            {
                // Create default preferences
                prefs = new UserPreferencesEntity { UserId = userId };
                _db.UserPreferences.Add(prefs);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }

            return MapToDto(prefs);
        }

        public async Task<UserPreferencesDto> UpdateAsync(Guid userId, UpdatePreferencesDto dto, CancellationToken token = default)
        {
            var prefs = await _db.UserPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId, token)
                .ConfigureAwait(false);

            if (prefs == null)
            {
                prefs = new UserPreferencesEntity
                {
                    UserId = userId,
                    Theme = dto.Theme,
                    DefaultLanguage = dto.DefaultLanguage,
                    CardSize = dto.CardSize,
                    NsfwVisibility = dto.NsfwVisibility
                };
                _db.UserPreferences.Add(prefs);
            }
            else
            {
                prefs.Theme = dto.Theme;
                prefs.DefaultLanguage = dto.DefaultLanguage;
                prefs.CardSize = dto.CardSize;
                prefs.NsfwVisibility = dto.NsfwVisibility;
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            return MapToDto(prefs);
        }

        private static UserPreferencesDto MapToDto(UserPreferencesEntity entity)
        {
            return new UserPreferencesDto
            {
                Theme = entity.Theme,
                DefaultLanguage = entity.DefaultLanguage,
                CardSize = entity.CardSize,
                NsfwVisibility = entity.NsfwVisibility
            };
        }
    }
}
