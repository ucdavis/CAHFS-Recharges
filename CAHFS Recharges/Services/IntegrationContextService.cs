using CAHFS_Recharges.Models;

namespace CAHFS_Recharges.Services
{
    public class IntegrationContextService : IIntegrationContextService
    {
        public const string CookieName = "CAEI.Integration";
        public const string RouteKey = "integration";

        private static readonly TimeSpan CookieExpiry = TimeSpan.FromDays(30);

        /// <inheritdoc />
        public IntegrationType? Resolve(HttpContext httpContext)
        {
            if (httpContext.Request.RouteValues.TryGetValue(RouteKey, out var routeValue))
            {
                if (IntegrationTypeExtensions.TryParse(routeValue?.ToString(), out var fromRoute))
                    return fromRoute;
            }

            var path = httpContext.Request.Path.Value ?? string.Empty;
            if (path.Contains("Equine", StringComparison.OrdinalIgnoreCase))
                return IntegrationType.EQUINE;

            if (path.Contains("FeedReview", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("SendToAggieEnterprise", StringComparison.OrdinalIgnoreCase))
                return IntegrationType.CAHFS;

            if (httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieValue))
            {
                if (IntegrationTypeExtensions.TryParse(cookieValue, out var fromCookie))
                    return fromCookie;
            }

            return null;
        }

        /// <inheritdoc />
        public void SetIntegrationCookie(HttpContext httpContext, IntegrationType integration)
        {
            var options = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.Add(CookieExpiry),
                IsEssential = true
            };

            httpContext.Response.Cookies.Append(CookieName, integration.ToString(), options);
        }

        /// <inheritdoc />
        public void ClearIntegrationCookie(HttpContext httpContext)
        {
            httpContext.Response.Cookies.Delete(CookieName);
        }
    }
}
