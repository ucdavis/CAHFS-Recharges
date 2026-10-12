using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Hiwu;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CAHFS_Recharges.Pages.Integrations.Hiwu
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class AttemptsModel : IntegrationPageModel
    {
        private readonly HiwuCoflFileOrderService _orders;

        public AttemptsModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            HiwuCoflFileOrderService orders)
            : base(integrationService, authorizationService)
        {
            _orders = orders;
        }

        public IReadOnlyList<HiwuCoflSampleRow> Rows { get; private set; } = [];

        public async Task<IActionResult> OnGetAsync()
        {
            if (CurrentIntegration != IntegrationType.EQUINE)
            {
                var pathBase = HttpContext.Request.PathBase.Value ?? "";
                return Redirect(pathBase + "/Integrations/EQUINE/Hiwu/Attempts");
            }

            Rows = await _orders.ListRecentAsync();
            return Page();
        }

        public static DateTime ToPacific(DateTime utc)
        {
            var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            return TimeZoneInfo.ConvertTimeFromUtc(value, zone);
        }
    }
}
