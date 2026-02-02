using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace CAHFS_Recharges.Services
{
    /// <summary>
    /// Protects /hangfire dashboard using the app's AuthorizationService policy.
    /// Default: requires the "CAHFSUser" policy.
    /// </summary>
    public sealed class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        private const string PolicyName = "CAHFSUser";

        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            // Must be authenticated first
            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
                return false;

            // Fail-closed if auth service isn't available (safer than allowing any authenticated user)
            var authz = httpContext.RequestServices.GetService<IAuthorizationService>();
            if (authz == null)
                return false;

            var result = authz.AuthorizeAsync(user, PolicyName).GetAwaiter().GetResult();
            return result.Succeeded;
        }
    }
}
