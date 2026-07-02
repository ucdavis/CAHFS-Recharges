using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using CAHFS_Recharges.Services;
using CAHFS_Recharges.Services.Lockbox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CAHFS_Recharges.Pages.Integrations.Lockbox
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class LockboxFilesModel : IntegrationPageModel
    {
        private readonly LockboxReadService _readService;

        public LockboxFilesModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            LockboxReadService readService)
            : base(integrationService, authorizationService)
        {
            _readService = readService;
        }

        private IntegrationType ResolvedIntegration => CurrentIntegration ?? IntegrationType.CAHFS;

        public LockboxStagingSummary Summary { get; private set; } = new();
        public IReadOnlyList<LockboxPaymentStagingRow> StagingRows { get; private set; } = Array.Empty<LockboxPaymentStagingRow>();

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? FileName { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (CurrentIntegration == null)
                return RedirectToPage("/Home");

            Summary = await _readService.GetStagingSummaryAsync(
                ResolvedIntegration, FromDate?.Date, ToDate?.Date, Status, FileName);

            StagingRows = await _readService.GetStagingRowsAsync(
                ResolvedIntegration, FromDate?.Date, ToDate?.Date, Status, FileName);

            return Page();
        }
    }
}
