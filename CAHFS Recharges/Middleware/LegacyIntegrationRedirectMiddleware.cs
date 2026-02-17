using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Http;

namespace CAHFS_Recharges.Middleware;

/// Redirects legacy integration URLs to route-based URLs so old bookmarks do not 404.
/// Preserves query string. Uses integration cookie when present for shared pages (CoaValidations, SentHistory).
public class LegacyIntegrationRedirectMiddleware
{
    private const string IntegrationCookieName = "CAEI.Integration";

    private readonly RequestDelegate _next;

    public LegacyIntegrationRedirectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var pathValue = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        var query = context.Request.QueryString;

        string? targetPath = null;

        if (pathValue.Equals("/Staging/FeedReview", StringComparison.OrdinalIgnoreCase))
            targetPath = "/Integrations/CAHFS/Staging/FeedReview";
        else if (pathValue.Equals("/Staging/EquineFeedReview", StringComparison.OrdinalIgnoreCase))
            targetPath = "/Integrations/EQUINE/Staging/FeedReview";
        else if (pathValue.Equals("/Staging/SendToAggieEnterprise", StringComparison.OrdinalIgnoreCase))
            targetPath = "/Integrations/CAHFS/Staging/SendToAggieEnterprise";
        else if (pathValue.Equals("/Staging/EquineSendToAggieEnterprise", StringComparison.OrdinalIgnoreCase))
            targetPath = "/Integrations/EQUINE/Staging/SendToAggieEnterprise";
        else if (pathValue.Equals("/Staging/CoaValidations", StringComparison.OrdinalIgnoreCase) ||
                 pathValue.Equals("/History/SentHistory", StringComparison.OrdinalIgnoreCase))
        {
            var integration = GetIntegrationFromCookie(context) ?? IntegrationType.CAHFS;
            if (pathValue.Equals("/Staging/CoaValidations", StringComparison.OrdinalIgnoreCase))
                targetPath = $"/Integrations/{integration}/Staging/CoaValidations";
            else
                targetPath = $"/Integrations/{integration}/History/SentHistory";
        }

        if (targetPath != null)
        {
            var pathBase = context.Request.PathBase.Value ?? "";
            context.Response.Redirect(pathBase + targetPath + query, permanent: false);
            return;
        }

        await _next(context);
    }

    private static IntegrationType? GetIntegrationFromCookie(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(IntegrationCookieName, out var value) &&
            IntegrationTypeExtensions.TryParse(value, out var result))
            return result;
        return null;
    }
}
