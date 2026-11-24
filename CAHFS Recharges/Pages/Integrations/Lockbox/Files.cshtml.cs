using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CAHFS_Recharges.Pages.Integrations.Lockbox
{
    /// Alias route so /Lockbox/Files hits the app (AccessDenied when gated) instead of IIS 404.
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class FilesModel : IntegrationPageModel
    {
        public FilesModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService)
            : base(integrationService, authorizationService)
        {
        }

        public IActionResult OnGet()
        {
            return RedirectToPage("./LockboxFiles", new { integration = RouteData.Values["integration"] });
        }
    }
}
