using CAHFS_Recharges.Models;
using Microsoft.Extensions.Configuration;

namespace CAHFS_Recharges.Services
{
    /// Helper for generating integration-aware page links.
    /// Always returns route-based paths (/Integrations/{integration}/...).
    public static class IntegrationLinkHelper
    {
        public static bool UseRouteBased { get; set; } = true;

        public static void Configure(IConfiguration configuration)
        {
            UseRouteBased = configuration.GetValue<bool?>("CAEI:UseRouteBased") ?? true;
        }

        private static IntegrationType EffectiveIntegration(IntegrationType? integration) =>
            integration ?? IntegrationType.CAHFS;

        public static string GetStagingDataPage(IntegrationType? integration)
        {
            var v = EffectiveIntegration(integration);
            return $"/Integrations/{v}/Staging/FeedReview";
        }

        public static string GetManualSendPage(IntegrationType? integration)
        {
            var v = EffectiveIntegration(integration);
            return $"/Integrations/{v}/Staging/SendToAggieEnterprise";
        }

        public static string GetCoaValidationPage(IntegrationType? integration)
        {
            var v = EffectiveIntegration(integration);
            return $"/Integrations/{v}/Staging/CoaValidations";
        }

        public static string GetSentHistoryPage(IntegrationType? integration)
        {
            var v = EffectiveIntegration(integration);
            return $"/Integrations/{v}/History/SentHistory";
        }

        public static string GetLockboxFilesPage(IntegrationType? integration)
        {
            var v = EffectiveIntegration(integration);
            return $"/Integrations/{v}/Lockbox/LockboxFiles";
        }

        public static string GetLockboxCheckValidationsPage(IntegrationType? integration)
        {
            var v = EffectiveIntegration(integration);
            return $"/Integrations/{v}/Lockbox/CheckValidations";
        }

        public static string GetLockboxSentHistoryPage(IntegrationType? integration)
        {
            var v = EffectiveIntegration(integration);
            return $"/Integrations/{v}/Lockbox/SentHistory";
        }

        public static string GetHiwuFilesPage(IntegrationType? integration)
        {
            return "/Integrations/EQUINE/Hiwu/Files";
        }

        public static string GetHiwuAttemptsPage(IntegrationType? integration)
        {
            return "/Integrations/EQUINE/Hiwu/Attempts";
        }

        public static string GetHiwuCreatePage(IntegrationType? integration)
        {
            return "/Integrations/EQUINE/Hiwu/Create";
        }

        public static string GetHiwuFileDetailPage(Guid fileId)
        {
            return $"/Integrations/EQUINE/Hiwu/Files/Detail?id={fileId:D}";
        }

        public static string AppendHiwuReturn(string url, string listPathAndQuery)
        {
            var separator = url.Contains('?') ? "&" : "?";
            return url + separator + "back=" + Uri.EscapeDataString(listPathAndQuery);
        }

        /// Keeps Back on a HIWU list or file. Anything else uses the fallback.
        public static string HiwuReturnPath(string? candidate, string fallback)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return fallback;

            var path = candidate.Trim();
            if (path.StartsWith("//", StringComparison.Ordinal)
                || path.Contains("://", StringComparison.Ordinal)
                || path.Contains('\\'))
                return fallback;
            if (!path.StartsWith('/'))
                path = "/" + path;

            var queryIndex = path.IndexOf('?');
            var route = queryIndex >= 0 ? path[..queryIndex] : path;
            var query = queryIndex >= 0 ? path[queryIndex..] : "";
            if (route.Contains("..", StringComparison.Ordinal))
                return fallback;

            if (string.Equals(route, "/Integrations/EQUINE/Hiwu/Files", StringComparison.OrdinalIgnoreCase)
                || string.Equals(route, "/Integrations/EQUINE/Hiwu/Create", StringComparison.OrdinalIgnoreCase)
                || string.Equals(route, "/Integrations/EQUINE/Hiwu/Attempts", StringComparison.OrdinalIgnoreCase))
                return route + query;

            if (string.Equals(route, "/Integrations/EQUINE/Hiwu/Files/Detail", StringComparison.OrdinalIgnoreCase)
                && QueryHasFileId(query)
                && !query.Contains("handler=", StringComparison.OrdinalIgnoreCase))
                return route + query;

            return fallback;
        }

        private static bool QueryHasFileId(string query)
        {
            var text = query.TrimStart('?');
            foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = part.IndexOf('=');
                if (split <= 0)
                    continue;
                var key = Uri.UnescapeDataString(part[..split]);
                if (!string.Equals(key, "id", StringComparison.OrdinalIgnoreCase))
                    continue;
                return Guid.TryParse(Uri.UnescapeDataString(part[(split + 1)..]), out _);
            }

            return false;
        }

        public static string GetDefaultLandingPage(IntegrationType integration, ProductFamily family) =>
            family switch
            {
                ProductFamily.Lockbox => GetLockboxFilesPage(integration),
                ProductFamily.Hiwu => GetHiwuFilesPage(IntegrationType.EQUINE),
                _ => GetStagingDataPage(integration)
            };

        /// Validates that the integration string is valid (CAHFS or EQUINE).
        public static IntegrationType? ValidateIntegration(string? integration)
        {
            if (IntegrationTypeExtensions.TryParse(integration, out var result))
                return result;

            return null;
        }

        public static string BuildIntegrationUrl(string basePath, IntegrationType? integration)
        {
            var v = EffectiveIntegration(integration);
            if (!basePath.StartsWith("/"))
                basePath = "/" + basePath;
            return $"/Integrations/{v}{basePath}";
        }
    }
}
