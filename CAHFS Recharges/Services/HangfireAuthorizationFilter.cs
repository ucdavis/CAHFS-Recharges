using CAHFS_Recharges.Authorization;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;

namespace CAHFS_Recharges.Services
{
    /// Protects /hangfire dashboard using the AdminPolicy.
    /// Only users in the Admins allowlist can access.
    /// Non-admins are redirected to /Denied.
    public sealed class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
               httpContext.Response.Redirect("/Login?returnUrl=/hangfire");
                return false;
            }

            var authz = httpContext.RequestServices.GetService<IAuthorizationService>();
            if (authz == null)
            {
                httpContext.Response.Redirect("/Denied?reason=hangfire");
                return false;
            }

            var result = authz.AuthorizeAsync(user, CaeiPolicies.AdminPolicy).GetAwaiter().GetResult();
            
            if (!result.Succeeded)
            {
                httpContext.Response.Redirect("/Denied?reason=hangfire");
                return false;
            }

            return true;
        }
    }
}
