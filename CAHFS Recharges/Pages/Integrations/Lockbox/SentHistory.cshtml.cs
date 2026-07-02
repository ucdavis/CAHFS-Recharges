using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;

namespace CAHFS_Recharges.Pages.Integrations.Lockbox
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class SentHistoryModel : IntegrationPageModel
    {
        public SentHistoryModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService)
            : base(integrationService, authorizationService)
        {
        }

        public void OnGet()
        {
        }
    }
}
