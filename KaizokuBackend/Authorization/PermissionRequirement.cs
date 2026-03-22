using Microsoft.AspNetCore.Authorization;

namespace KaizokuBackend.Authorization
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string Permission { get; }

        /// <summary>
        /// All permissions that satisfy this requirement (OR logic).
        /// For single-permission policies this contains just one entry.
        /// For comma-separated policies (e.g. "CanEditSeries,CanDeleteSeries")
        /// the user needs ANY ONE of these permissions.
        /// </summary>
        public string[] Permissions { get; }

        public PermissionRequirement(string permission)
        {
            Permission = permission;
            Permissions = permission.Contains(',')
                ? permission.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : new[] { permission };
        }
    }
}
