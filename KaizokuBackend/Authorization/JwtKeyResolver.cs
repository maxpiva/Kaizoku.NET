using KaizokuBackend.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace KaizokuBackend.Authorization
{
    /// <summary>
    /// Resolves the JWT signing key from the database at runtime.
    /// Implements IPostConfigureOptions to set the IssuerSigningKeyResolver
    /// after the service provider is built.
    /// </summary>
    public class JwtKeyResolver : IPostConfigureOptions<JwtBearerOptions>
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private SymmetricSecurityKey? _cachedKey;

        public JwtKeyResolver(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public void PostConfigure(string? name, JwtBearerOptions options)
        {
            options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
            {
                if (_cachedKey != null)
                    return new[] { _cachedKey };

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var setting = db.Settings.FirstOrDefault(s => s.Name == "JwtSecret");
                    if (setting == null)
                        return Array.Empty<SecurityKey>();

                    _cachedKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(setting.Value));
                    return new[] { _cachedKey };
                }
                catch
                {
                    return Array.Empty<SecurityKey>();
                }
            };
        }
    }
}
