using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CAHFS_Recharges.Pages
{
    [Authorize]
    public class HomeModel : PageModel
    {
        private readonly IIntegrationContextService _integrationService;

        public string Username => User.Identity?.Name ?? "User";

        public HomeModel(IIntegrationContextService integrationService)
        {
            _integrationService = integrationService;
        }

        public void OnGet()
        {
            // Home page just displays integration selection
        }

        public IActionResult OnPostSelectIntegration(string integration)
        {
            if (!IntegrationTypeExtensions.TryParse(integration, out var integrationType))
            {
                return RedirectToPage();
            }

            _integrationService.SetIntegrationCookie(HttpContext, integrationType);

            // Always redirect to route-based URL: /Integrations/{integration}/Staging/FeedReview
            var redirectUrl = IntegrationLinkHelper.GetStagingDataPage(integrationType);
            return Redirect(redirectUrl);
        }
    }
}
