using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Hiwu;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CAHFS_Recharges.Pages.Integrations.Hiwu
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class CreateModel : IntegrationPageModel
    {
        private readonly HiwuCoflFileOrderService _orders;

        public CreateModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            HiwuCoflFileOrderService orders)
            : base(integrationService, authorizationService)
        {
            _orders = orders;
        }

        [BindProperty(SupportsGet = true)]
        public string? FileName { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool NotStarted { get; set; } = true;

        [BindProperty(SupportsGet = true)]
        public bool ShowPartial { get; set; } = true;

        [BindProperty(SupportsGet = true)]
        public bool Failed { get; set; }

        public IReadOnlyList<HiwuCoflWorkItem> Items { get; private set; } = [];

        public async Task<IActionResult> OnGetAsync()
        {
            if (CurrentIntegration != IntegrationType.EQUINE)
            {
                var pathBase = HttpContext.Request.PathBase.Value ?? "";
                return Redirect(pathBase + "/Integrations/EQUINE/Hiwu/Create" + Request.QueryString.Value);
            }

            Items = await _orders.ListWorkAsync(FileName, NotStarted, ShowPartial, Failed);
            return Page();
        }

        public static DateTime ToPacific(DateTime utc) => FilesModel.ToPacific(utc);
    }
}
