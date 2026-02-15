using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace CAHFS_Recharges.Authorization
{
    /// Authorization handler that checks username against config allowlists.
    /// Role hierarchy: Admin > Operator > Viewer.
    public class ConfigBasedRoleHandler : AuthorizationHandler<RoleRequirement>
    {
        private readonly HashSet<string> _admins;
        private readonly HashSet<string> _operators;

        public ConfigBasedRoleHandler(IConfiguration configuration)
        {
            // Load allowlists from config (case-insensitive)
            _admins = configuration.GetSection("Authorization:Admins")
                .Get<string[]>()
                ?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _operators = configuration.GetSection("Authorization:Operators")
                .Get<string[]>()
                ?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RoleRequirement requirement)
        {
            // Must be authenticated
            if (context.User?.Identity?.IsAuthenticated != true)
                return Task.CompletedTask;

            var username = context.User.Identity.Name;
            if (string.IsNullOrWhiteSpace(username))
                return Task.CompletedTask;

            var userRole = GetUserRole(username);

            // Check if user's role satisfies the requirement
            if (RoleSatisfiesRequirement(userRole, requirement.RequiredRole))
                context.Succeed(requirement);

            return Task.CompletedTask;
        }

        /// Determines user's role from config lists.
        private string GetUserRole(string username)
        {
            if (_admins.Contains(username))
                return CaeiRoles.Admin;

            if (_operators.Contains(username))
                return CaeiRoles.Operator;

            // Default: any authenticated user is Viewer
            return CaeiRoles.Viewer;
        }

        /// Checks if user's role meets or exceeds the required role.
        private static bool RoleSatisfiesRequirement(string userRole, string requiredRole)
        {
            return requiredRole switch
            {
                CaeiRoles.Viewer => true, // Everyone authenticated is at least Viewer
                CaeiRoles.Operator => userRole is CaeiRoles.Operator or CaeiRoles.Admin,
                CaeiRoles.Admin => userRole == CaeiRoles.Admin,
                _ => false
            };
        }
    }
}
