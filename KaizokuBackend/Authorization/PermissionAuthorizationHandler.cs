using KaizokuBackend.Models.Enums;
using Microsoft.AspNetCore.Authorization;

namespace KaizokuBackend.Authorization
{
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                return Task.CompletedTask;
            }

            // Admins always have all permissions
            var roleClaim = context.User.FindFirst("Role")?.Value;
            if (roleClaim != null && Enum.TryParse<UserRole>(roleClaim, out var role) && role == UserRole.Admin)
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // Check permission claims — user needs ANY ONE of the required permissions
            foreach (var permission in requirement.Permissions)
            {
                var permissionClaim = context.User.FindFirst(permission)?.Value;
                if (permissionClaim != null && bool.TryParse(permissionClaim, out var hasPermission) && hasPermission)
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }

            return Task.CompletedTask;
        }
    }

    public class AdminRequirement : IAuthorizationRequirement { }

    public class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
        {
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                return Task.CompletedTask;
            }

            var roleClaim = context.User.FindFirst("Role")?.Value;
            if (roleClaim != null && Enum.TryParse<UserRole>(roleClaim, out var role) && role == UserRole.Admin)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
