using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace KaizokuBackend.Authorization
{
    public class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private const string RequirePermissionPrefix = "RequirePermission:";
        private const string RequireAdminPolicy = "RequireAdmin";
        private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;

        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        {
            _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        {
            return _fallbackPolicyProvider.GetDefaultPolicyAsync();
        }

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        {
            return _fallbackPolicyProvider.GetFallbackPolicyAsync();
        }

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName == RequireAdminPolicy)
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AdminRequirement())
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }

            if (policyName.StartsWith(RequirePermissionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var permission = policyName.Substring(RequirePermissionPrefix.Length);
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }

            return _fallbackPolicyProvider.GetPolicyAsync(policyName);
        }
    }
}
