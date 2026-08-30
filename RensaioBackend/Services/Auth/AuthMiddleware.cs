using java.nio.file;
using Microsoft.EntityFrameworkCore;
using RensaioBackend.Data;
using RensaioBackend.Models.Database;
using System.Security.Claims;

namespace RensaioBackend.Services.Auth;

/// <summary>
/// Authentication middleware that handles both JWT-based auth and header-based user selection.
/// </summary>
public class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
    {
        //string pathStr = context.Request.Path.Value?.TrimEnd('/') ?? "";

        // Always allow routes that use path-based user resolution (OPDS, MCP)
        if (IsBypassRoute(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Skip auth for public endpoints
        if (IsPublicRoute(context.Request.Path, context.Request.Method))
        {
            await _next(context);
            return;
        }
        /*
        if (!pathStr.StartsWith("/api/"))
        {
            await _next(context);
            return;
        }*/
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<Services.Settings.SettingsService>();

        bool authEnabled = await IsAuthenticationEnabled(settings);

        if (authEnabled)
        {
            // JWT-based authentication
            string? authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string token = authHeader["Bearer ".Length..].Trim();
            var jwtService = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
            ClaimsPrincipal? principal = jwtService.ValidateToken(token);

            if (principal == null)
            {
                context.Response.StatusCode = 401;
                return;
            }

            Guid? userId = jwtService.GetUserIdFromPrincipal(principal);
            if (userId == null)
            {
                context.Response.StatusCode = 401;
                return;
            }

            UserEntity? user = await db.Users.FindAsync(userId.Value);
            if (user == null || !user.IsActive)
            {
                context.Response.StatusCode = 401;
                return;
            }

            context.Items["User"] = user;
            context.Items["AuthEnabled"] = true;
        }
        else
        {
            // Header-based user selection (auth disabled)
            string? username = context.Request.Headers["X-Rensaio-User"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(username))
            {
                UserEntity? user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
                if (user != null)
                {
                    context.Items["User"] = user;
                }
            }

            context.Items["AuthEnabled"] = false;
        }

        await _next(context);
    }

    private static bool IsBypassRoute(PathString path)
    {
        // Routes that use path-based user resolution (OPDS, MCP) bypass JWT auth.
        // The user is resolved from the {opdsPath} segment by the controller itself.
        // We can't easily distinguish OPDS routes by prefix, but MCP routes have a
        // recognizable /mcp/ segment.
        var pathStr = path.Value?.Trim('/') ?? "";
        var segments = pathStr.Split('/');
        // MCP: /{opdsPath}/mcp/sse or /{opdsPath}/mcp/message
        if (segments.Length >= 2 && segments[1].Equals("mcp", StringComparison.OrdinalIgnoreCase))
            return true;
        // OPDS: anything that doesn't start with /api/ is treated as OPDS
        // Since we can't distinguish easily, we handle OPDS routes in the middleware below
        // by allowing all routes through if auth is disabled or if the route doesn't start with /api/
        return false;
    }

    private static bool IsPublicRoute(PathString path, string method)
    {
        string pathStr = path.Value?.TrimEnd('/') ?? "";

        // Public auth endpoints
        if (pathStr.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase)) return true;
        if (pathStr.StartsWith("/api/auth/status", StringComparison.OrdinalIgnoreCase)) return true;
        if (pathStr.StartsWith("/api/auth/select-user", StringComparison.OrdinalIgnoreCase)) return true;
        if (pathStr.StartsWith("/api/auth/refresh", StringComparison.OrdinalIgnoreCase)) return true;
        if (pathStr.StartsWith("/api/auth/set-password", StringComparison.OrdinalIgnoreCase)) return true;

        // First-user creation (only when no users exist)
        if (pathStr.StartsWith("/api/users/first", StringComparison.OrdinalIgnoreCase) && method == "POST") return true;
        if (pathStr.StartsWith("/api/users/", StringComparison.OrdinalIgnoreCase) && pathStr.EndsWith("/claim", StringComparison.OrdinalIgnoreCase) && method == "PUT") return true;

        // Image endpoints always bypass auth so images (thumbnails, OPDS pages,
        // cached images) flow regardless of authentication state. They are loaded
        // from <img> tags and other non-browser contexts that cannot send headers.
        if (pathStr.StartsWith("/api/image", StringComparison.OrdinalIgnoreCase)) return true;
        if (IsImageRoute(path)) return true;

        return false;
    }

    /// <summary>
    /// Detects OPDS image/thumbnail routes: /{opdsPath}/image/... and /{opdsPath}/thumb/...
    /// These are exempt from auth so that image loads (page images, cached images,
    /// thumbnails) work without a Bearer token.
    /// </summary>
    private static bool IsImageRoute(PathString path)
    {
        var pathStr = path.Value?.Trim('/') ?? "";
        var segments = pathStr.Split('/');
        if (segments.Length < 2)
            return false;
        return segments[1].Equals("image", StringComparison.OrdinalIgnoreCase)
            || segments[1].Equals("thumb", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> IsAuthenticationEnabled(Services.Settings.SettingsService settings)
    {
        try
        {
            var editableSettings = await settings.GetSettingsAsync();
            return editableSettings.AuthenticationEnabled;
        }
        catch
        {
            return false;
        }
    }
}

public static class AuthMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthMiddleware>();
    }
}