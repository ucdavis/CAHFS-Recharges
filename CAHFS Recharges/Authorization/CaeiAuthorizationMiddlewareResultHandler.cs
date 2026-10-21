using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace CAHFS_Recharges.Authorization
{
    /// Maps authorization Forbid for authenticated-but-not-allowlisted users to /AccessDenied?reason=app.
    public sealed class CaeiAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public async Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden && context.User?.Identity?.IsAuthenticated == true)
            {
                var pathBase = context.Request.PathBase.Value ?? string.Empty;
                context.Response.Redirect(pathBase.TrimEnd('/') + "/AccessDenied?reason=app");
                return;
            }

            await _default.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
