using CAHFS_Recharges.Models;

namespace CAHFS_Recharges.Services
{
    public class IntegrationContextService : IIntegrationContextService
    {
        public const string CookieName = "CAEI.Integration";
        public const string ProductFamilyCookieName = "CAEI.ProductFamily";
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
        public ProductFamily? ResolveProductFamily(HttpContext httpContext)
        {
            var path = httpContext.Request.Path.Value ?? string.Empty;
            if (path.Contains("/Hiwu/", StringComparison.OrdinalIgnoreCase))
                return ProductFamily.Hiwu;

            if (path.Contains("/Lockbox/", StringComparison.OrdinalIgnoreCase))
                return ProductFamily.Lockbox;

            if (path.Contains("/Staging/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/History/", StringComparison.OrdinalIgnoreCase))
                return ProductFamily.AE;

            if (httpContext.Request.Cookies.TryGetValue(ProductFamilyCookieName, out var cookieValue) &&
                ProductFamilyExtensions.TryParse(cookieValue, out var fromCookie))
                return fromCookie;

            return null;
        }

        /// <inheritdoc />
        public void SetIntegrationCookie(HttpContext httpContext, IntegrationType integration)
        {
            httpContext.Response.Cookies.Append(CookieName, integration.ToString(), CookieWriteOptions(httpContext));
        }

        /// <inheritdoc />
        public void ClearIntegrationCookie(HttpContext httpContext)
        {
            HttpHelper.ExpireCookie(httpContext.Response, httpContext.Request, CookieName);
        }

        /// <inheritdoc />
        public void SetProductFamilyCookie(HttpContext httpContext, ProductFamily productFamily)
        {
            httpContext.Response.Cookies.Append(ProductFamilyCookieName, productFamily.ToString(), CookieWriteOptions(httpContext));
        }

        /// <inheritdoc />
        public void ClearProductFamilyCookie(HttpContext httpContext)
        {
            HttpHelper.ExpireCookie(httpContext.Response, httpContext.Request, ProductFamilyCookieName);
        }

        private static CookieOptions CookieWriteOptions(HttpContext httpContext) => new()
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.Add(CookieExpiry),
            IsEssential = true,
            Path = HttpHelper.CookiePath(httpContext.Request)
        };
    }
}
