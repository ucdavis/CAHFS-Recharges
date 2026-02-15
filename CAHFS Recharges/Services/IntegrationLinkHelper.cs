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
