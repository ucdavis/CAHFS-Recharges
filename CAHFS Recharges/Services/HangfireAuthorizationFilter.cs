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
            var pathBase = httpContext.Request.PathBase.Value ?? "";

            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                httpContext.Response.Redirect(pathBase + "/Login?returnUrl=" + Uri.EscapeDataString(pathBase + "/hangfire"));
                return false;
            }

            var authz = httpContext.RequestServices.GetService<IAuthorizationService>();
            if (authz == null)
            {
                httpContext.Response.Redirect(pathBase + "/Denied?reason=hangfire");
                return false;
            }

            var result = authz.AuthorizeAsync(user, CaeiPolicies.AdminPolicy).GetAwaiter().GetResult();
            
            if (!result.Succeeded)
            {
                httpContext.Response.Redirect(pathBase + "/Denied?reason=hangfire");
                return false;
            }

            return true;
        }
    }
}
