using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using CAHFS_Recharges.Services;
using CAHFS_Recharges.Services.Lockbox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CAHFS_Recharges.Pages.Integrations.Lockbox
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class CheckValidationsModel : IntegrationPageModel
    {
        private readonly LockboxReadService _readService;
        private readonly ILogger<CheckValidationsModel> _logger;

        public CheckValidationsModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            LockboxReadService readService,
            ILogger<CheckValidationsModel> logger)
            : base(integrationService, authorizationService)
        {
            _readService = readService;
            _logger = logger;
        }

        private IntegrationType ResolvedIntegration => CurrentIntegration ?? IntegrationType.CAHFS;

        public LockboxCheckValidationSummary Summary { get; private set; } = new();
        public IReadOnlyList<LockboxCheckValidationRow> Rows { get; private set; } = Array.Empty<LockboxCheckValidationRow>();

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        /// <summary>Empty = exceptions (INVALID_CUSTOMER, WARN, ERROR).</summary>
        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? BillingId { get; set; }

        [BindProperty]
        public int StagingId { get; set; }

        [BindProperty]
        public string? EditedBillingId { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (CurrentIntegration == null)
                return RedirectToPage("/Home");

            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostSaveBillingIdAsync()
        {
            if (CurrentIntegration == null)
                return RedirectToPage("/Home");

            if (!await IsOperatorAsync())
            {
                SetErrorMessage("Operator access is required to correct Billing ID.");
                return RedirectToPage(new
                {
                    integration = Integration,
                    FromDate = FromDate?.ToString("yyyy-MM-dd"),
                    ToDate = ToDate?.ToString("yyyy-MM-dd"),
                    Status,
                    BillingId
                });
            }

            var result = await _readService.UpdateBillingIdAsync(
                ResolvedIntegration,
                StagingId,
                EditedBillingId,
                User.Identity?.Name ?? "unknown");

            if (result.Success)
            {
                SetSuccessMessage(result.Message);
                _logger.LogInformation(
                    "Lockbox BillingId update StagingId={StagingId} Integration={Integration} BecameValid={BecameValid} User={User}",
                    StagingId, ResolvedIntegration, result.BecameValid, User.Identity?.Name);
            }
            else
            {
                SetErrorMessage(result.Message);
            }

            return RedirectToPage(new
            {
                integration = Integration,
                FromDate = FromDate?.ToString("yyyy-MM-dd"),
                ToDate = ToDate?.ToString("yyyy-MM-dd"),
                Status,
                BillingId
            });
        }

        private async Task LoadAsync()
        {
            Summary = await _readService.GetCheckValidationSummaryAsync(
                ResolvedIntegration, FromDate?.Date, ToDate?.Date, BillingId);

            Rows = await _readService.GetCheckValidationRowsAsync(
                ResolvedIntegration, FromDate?.Date, ToDate?.Date, Status, BillingId);
        }
    }
}
