using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Pages.Integrations.Staging
{
    /// <summary>
    /// Unified Manual Send to AE page for both CAHFS and EQUINE integrations.
    /// Uses route parameter {integration} to determine which database to use.
    /// Route: /Integrations/{integration}/Staging/SendToAggieEnterprise
    /// </summary>
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class SendToAggieEnterpriseModel : IntegrationPageModel
    {
        private readonly IIntegrationDbResolver _dbResolver;
        private readonly AggieEnterpriseSendGatekeeper _gatekeeper;
        private readonly AggieEnterpriseJournalUploadService _upload;

        public SendToAggieEnterpriseModel(
            IIntegrationDbResolver dbResolver,
            AggieEnterpriseSendGatekeeper gatekeeper,
            AggieEnterpriseJournalUploadService upload,
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService)
            : base(integrationService, authorizationService)
        {
            _dbResolver = dbResolver;
            _gatekeeper = gatekeeper;
            _upload = upload;
        }

        /// Gets the current integration type (resolved from route).
        private IntegrationType ResolvedIntegration => CurrentIntegration ?? IntegrationType.CAHFS;

        [TempData]
        public bool ShowPreview { get; set; }

        [TempData]
        public Guid? PreviewForBatchId { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? JournalName { get; set; }

        [BindProperty(SupportsGet = true)]
        public Guid? SelectedBatchId { get; set; }

        public IList<FeedBatch> Batches { get; set; } = new List<FeedBatch>();
        public FeedBatch? SelectedBatch { get; set; }

        public string? AccountingDateText { get; set; }
        public string? AccountingPeriodText { get; set; }

        public bool GateCanSend { get; set; }
        public string GateMessage { get; set; } = "Select a batch.";
        public AggieEnterpriseSendGatekeeper.GateSummary? GateSummary { get; set; }

        public AggieEnterpriseJournalUploadService.PreviewResult? Preview { get; set; }

        public DateTime LastRefreshedAt { get; set; } = DateTime.Now;

        /// <summary>Count of batches successfully sent (Validated, Complete, Completed, Success) for the Sent History card.</summary>
        public int SuccessfullySentCount { get; set; }

        /// <summary>Count of batches that currently have invalid COA items (for the COA Validations summary card).</summary>
        public int InvalidBatchCount { get; set; }

        public async Task OnGetAsync()
        {
            await LoadPageAsync(loadPreview: false);

            if (ShowPreview && SelectedBatchId.HasValue && PreviewForBatchId.HasValue
                && PreviewForBatchId.Value == SelectedBatchId.Value)
            {
                Preview = await _upload.BuildPreviewAsync(SelectedBatchId.Value, ResolvedIntegration);
            }
            else
            {
                Preview = null;
            }

            ShowPreview = false;
        }

        public async Task<IActionResult> OnPostBuildPreviewAsync()
        {
            if (!await IsOperatorAsync())
            {
                SetErrorMessage("You do not have permission to perform this action.");
                return RedirectToThis();
            }

            await LoadPageAsync(loadPreview: false);

            if (!SelectedBatchId.HasValue)
            {
                SetErrorMessage("Please select a batch.");
                return RedirectToThis();
            }

            PreviewForBatchId = SelectedBatchId.Value;
            ShowPreview = true;
            SetInfoMessage("Preview built.");

            return RedirectToThis();
        }

        public async Task<IActionResult> OnPostSendToAeAsync()
        {
            if (!await IsOperatorAsync())
            {
                SetErrorMessage("You do not have permission to perform this action.");
                return RedirectToThis();
            }

            await LoadPageAsync(loadPreview: false);

            if (!SelectedBatchId.HasValue)
            {
                SetErrorMessage("Please select a batch.");
                return RedirectToThis();
            }

            var result = await _upload.SendBatchAsync(SelectedBatchId.Value, ResolvedIntegration);
            if (result.Success)
            {
                SetSuccessMessage(result.Message);
            }
            else
            {
                SetErrorMessage(result.Message);
            }

            ShowPreview = false;
            PreviewForBatchId = null;

            return RedirectToThis();
        }

        public async Task<IActionResult> OnPostRefreshStatusAsync()
        {
            if (!await IsOperatorAsync())
            {
                SetErrorMessage("You do not have permission to perform this action.");
                return RedirectToThis();
            }

            await LoadPageAsync(loadPreview: false);

            if (!SelectedBatchId.HasValue)
            {
                SetErrorMessage("Please select a batch.");
                return RedirectToThis();
            }

            var result = await _upload.CheckStatusAsync(SelectedBatchId.Value, ResolvedIntegration);
            if (result.Success)
            {
                SetInfoMessage(result.Message);
            }
            else
            {
                SetErrorMessage(result.Message);
            }

            return RedirectToThis();
        }

        public async Task<IActionResult> OnGetDownloadPayloadAsync()
        {
            if (!SelectedBatchId.HasValue)
                return RedirectToThis();

            var json = await _upload.BuildPayloadJsonAsync(SelectedBatchId.Value, ResolvedIntegration);
            var fileName = $"glJournalRequest_{ResolvedIntegration}_{SelectedBatchId.Value}_{DateTime.UtcNow:yyyyMMddHHmmss}.json";

            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
        }

        private async Task LoadPageAsync(bool loadPreview)
        {
            ResolveSelectedBatchIdFromRequest();

            if (!HttpMethods.IsPost(Request.Method))
            {
                if (PreviewForBatchId.HasValue && SelectedBatchId.HasValue &&
                    PreviewForBatchId.Value != SelectedBatchId.Value)
                {
                    ShowPreview = false;
                    PreviewForBatchId = null;
                }
            }

            // Use resolver to get correct DbSets
            var feedBatches = _dbResolver.GetFeedBatches(ResolvedIntegration);
            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);

            var q = feedBatches.AsQueryable();

            if (FromDate.HasValue)
            {
                var from = FromDate.Value.Date;
                q = q.Where(b => (b.AETransactionDate ?? b.DateSent) >= from);
            }

            if (ToDate.HasValue)
            {
                var to = ToDate.Value.Date.AddDays(1);
                q = q.Where(b => (b.AETransactionDate ?? b.DateSent) < to);
            }

            // For this page, only show batches that are Ready to send.
            q = q.Where(b => b.AERequestStatus != null && b.AERequestStatus.Trim() == "Ready");

            if (!string.IsNullOrWhiteSpace(JournalName))
            {
                var j = JournalName.Trim();
                q = q.Where(b => b.AEJournalName.Contains(j));
            }

            Batches = await q
                .OrderByDescending(b => b.AETransactionDate ?? b.DateSent)
                .ThenByDescending(b => b.BatchID)
                .Take(200)
                .AsNoTracking()
                .ToListAsync();

            // Count successfully sent batches (same logic as Sent History "Successful")
            SuccessfullySentCount = await feedBatches
                .AsNoTracking()
                .CountAsync(b => b.AERequestStatus != null &&
                    (b.AERequestStatus.Trim() == "Validated" ||
                     b.AERequestStatus.Trim() == "Complete" ||
                     b.AERequestStatus.Trim() == "Completed" ||
                     b.AERequestStatus.Trim() == "Success"));

            // Count batches that have at least one invalid item (for COA Validations card)
            InvalidBatchCount = await feedItems
                .AsNoTracking()
                .Where(i => i.DebitStringValid == "Invalid" || i.CreditStringValid == "Invalid")
                .Select(i => i.BatchID)
                .Distinct()
                .CountAsync();

            var hasQuery = Request.Query.ContainsKey("SelectedBatchId");
            var hasForm = HttpMethods.IsPost(Request.Method) && Request.Form.ContainsKey("SelectedBatchId");

            // First-load default: if nothing explicitly selected, pick the first available batch
            if (!hasQuery && !hasForm && !SelectedBatchId.HasValue && Batches.Any())
            {
                SelectedBatchId = Batches.First().BatchID;
                ShowPreview = false;
                PreviewForBatchId = null;
            }

            // Auto-advance behavior: if the previously selected batch is no longer in the
            // current filtered list (e.g. it was Sent and dropped out of Ready),
            // move selection to the first remaining batch (if any).
            if (SelectedBatchId.HasValue && !Batches.Any(b => b.BatchID == SelectedBatchId.Value))
            {
                if (Batches.Any())
                {
                    SelectedBatchId = Batches.First().BatchID;
                }
                else
                {
                    SelectedBatchId = null;
                }

                ShowPreview = false;
                PreviewForBatchId = null;
            }

            if (SelectedBatchId.HasValue)
            {
                SelectedBatch = await feedBatches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BatchID == SelectedBatchId.Value);

                if (SelectedBatch != null)
                {
                    var acctDt = (SelectedBatch.AETransactionDate ?? SelectedBatch.DateSent ?? DateTime.UtcNow).Date;
                    AccountingDateText = acctDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    AccountingPeriodText = acctDt.ToString("MMM-yy", CultureInfo.InvariantCulture);

                    var gate = await _gatekeeper.CanSendBatchAsync(SelectedBatchId.Value, ResolvedIntegration);
                    GateCanSend = gate.CanSend;
                    GateMessage = gate.Message;
                    GateSummary = gate.Summary;
                }
                else
                {
                    GateCanSend = false;
                    GateMessage = "Selected batch not found.";
                    GateSummary = null;

                    ShowPreview = false;
                    PreviewForBatchId = null;
                }
            }
            else
            {
                SelectedBatch = null;
                GateCanSend = false;
                GateMessage = "Select a batch.";
                GateSummary = null;

                ShowPreview = false;
                PreviewForBatchId = null;
            }

            if (loadPreview && SelectedBatchId.HasValue)
            {
                Preview = await _upload.BuildPreviewAsync(SelectedBatchId.Value, ResolvedIntegration);
                PreviewForBatchId = SelectedBatchId.Value;
                ShowPreview = true;
            }
        }

        private void ResolveSelectedBatchIdFromRequest()
        {
            if (HttpMethods.IsPost(Request.Method))
            {
                var rawForm = Request.Form["SelectedBatchId"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(rawForm) && Guid.TryParse(rawForm, out var g))
                {
                    SelectedBatchId = g;
                    return;
                }
            }

            var rawQuery = Request.Query["SelectedBatchId"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(rawQuery) && Guid.TryParse(rawQuery, out var g2))
            {
                SelectedBatchId = g2;
                return;
            }

            SelectedBatchId = null;
        }

        private IActionResult RedirectToThis()
        {
            return RedirectToPage(new
            {
                Integration,  // Preserve route parameter
                FromDate = FromDate?.ToString("yyyy-MM-dd"),
                ToDate = ToDate?.ToString("yyyy-MM-dd"),
                JournalName,
                SelectedBatchId
            });
        }
    }
}
