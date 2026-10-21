using Microsoft.AspNetCore.Authorization;

namespace CAHFS_Recharges.Authorization
{
    /// Authorization handler that evaluates RoleRequirement via ICaeiRoleResolver.
    /// Fail closed: unlisted CAS users do not satisfy Viewer (or any role).
    public class ConfigBasedRoleHandler : AuthorizationHandler<RoleRequirement>
    {
        private readonly ICaeiRoleResolver _roleResolver;

        public ConfigBasedRoleHandler(ICaeiRoleResolver roleResolver)
        {
            _roleResolver = roleResolver;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RoleRequirement requirement)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
                return Task.CompletedTask;

            var username = context.User.Identity.Name;
            var userRole = _roleResolver.TryResolveRole(username);
            if (userRole == null)
                return Task.CompletedTask;

            if (RoleSatisfiesRequirement(userRole.Value, requirement.RequiredRole))
                context.Succeed(requirement);

            return Task.CompletedTask;
        }

        private static bool RoleSatisfiesRequirement(CaeiRole userRole, string requiredRole)
        {
            return requiredRole switch
            {
                CaeiRoles.Viewer => true,
                CaeiRoles.Operator => userRole is CaeiRole.Operator or CaeiRole.Admin,
                CaeiRoles.Admin => userRole == CaeiRole.Admin,
                _ => false
            };
        }
    }
}
