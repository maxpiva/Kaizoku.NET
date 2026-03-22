using KaizokuBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Authorization
{
    /// <summary>
    /// Middleware that enables bootstrap mode when no users exist in the database.
    /// In bootstrap mode, the setup wizard and auth/setup endpoints are accessible without authentication,
    /// and all other protected endpoints return 403 with a setup-required message.
    /// </summary>
    public class BootstrapModeMiddleware
    {
        private readonly RequestDelegate _next;
        private static bool? _hasUsers;

        public BootstrapModeMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext db)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

            // Always allow these paths without any check
            if (path.StartsWith("/api/auth/status") ||
                path.StartsWith("/api/auth/setup") ||
                path.StartsWith("/api/auth/login") ||
                path.StartsWith("/api/auth/register") ||
                path.StartsWith("/api/auth/refresh") ||
                path.StartsWith("/api/invites/validate/"))
            {
                await _next(context);
                return;
            }

            // Check if users exist (cached to avoid DB hit on every request)
            if (_hasUsers == null || _hasUsers == false)
            {
                _hasUsers = await db.Users.AnyAsync();
            }

            if (_hasUsers == false)
            {
                // In bootstrap mode, allow setup wizard and static files
                if (path.StartsWith("/api/setup") ||
                    path.StartsWith("/api/settings") ||
                    !path.StartsWith("/api/"))
                {
                    await _next(context);
                    return;
                }

                // Block all other API endpoints
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Setup required. Please create an admin account first.\"}");
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// Invalidate the cached user check so it re-queries on next request.
        /// Call this after the first user is created.
        /// </summary>
        public static void InvalidateCache()
        {
            _hasUsers = null;
        }
    }
}
