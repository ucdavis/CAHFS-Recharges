using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Hiwu;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CAHFS_Recharges.Pages.Integrations.Hiwu
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class CreateReviewModel : IntegrationPageModel
    {
        private readonly HiwuReadService _readService;
        private readonly HiwuCoflFileOrderService _orders;

        public CreateReviewModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            HiwuReadService readService,
            HiwuCoflFileOrderService orders)
            : base(integrationService, authorizationService)
        {
            _readService = readService;
            _orders = orders;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public HiwuImportFile? Manifest { get; private set; }
        public HiwuCoflWorkReview? Review { get; private set; }
        public string? ResultMessage { get; private set; }
        public bool IsOperatorUser { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostCreateOrderAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;
            if (!IsOperator())
                return Forbid();

            Manifest = await _readService.GetFileAsync(Id);
            if (Manifest == null)
                return Page();

            var result = await _orders.CreateOrderAsync(Manifest);
            ResultMessage = result.Message;
            Review = await _orders.ReviewAsync(Manifest);
            IsOperatorUser = true;
            return Page();
        }

        private async Task LoadAsync()
        {
            IsOperatorUser = IsOperator();
            Manifest = await _readService.GetFileAsync(Id);
            if (Manifest != null)
                Review = await _orders.ReviewAsync(Manifest);
        }

        private IActionResult? RedirectCahfs()
        {
            if (CurrentIntegration == IntegrationType.EQUINE)
                return null;

            var pathBase = HttpContext.Request.PathBase.Value ?? "";
            return Redirect(pathBase + "/Integrations/EQUINE/Hiwu/Create/Review" + Request.QueryString.Value);
        }
    }
}
