using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Pages.Staging
{
    // Optional but helpful: avoid caching
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class SendToAggieEnterpriseModel : PageModel
    {
        private readonly FinancialContext _db;
        private readonly AggieEnterpriseSendGatekeeper _gatekeeper;
        private readonly AggieEnterpriseJournalUploadService _upload;

        public SendToAggieEnterpriseModel(
            FinancialContext db,
            AggieEnterpriseSendGatekeeper gatekeeper,
            AggieEnterpriseJournalUploadService upload)
        {
            _db = db;
            _gatekeeper = gatekeeper;
            _upload = upload;
        }

        [TempData]
        public string? PageMessage { get; set; }

        //Controls whether preview should be shown on the next GET
        [TempData]
        public bool ShowPreview { get; set; }

        //Which batch that preview belongs to
        [TempData]
        public Guid? PreviewForBatchId { get; set; }

        // Filters
        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? JournalName { get; set; }

        // Selected Batch
        [BindProperty(SupportsGet = true)]
        public Guid? SelectedBatchId { get; set; }

        public IList<FeedBatch> Batches { get; set; } = new List<FeedBatch>();
        public FeedBatch? SelectedBatch { get; set; }

        // Summary strings
        public string? AccountingDateText { get; set; }
        public string? AccountingPeriodText { get; set; }

        // Gatekeeping UI
        public bool GateCanSend { get; set; }
        public string GateMessage { get; set; } = "Select a batch.";
        public AggieEnterpriseSendGatekeeper.GateSummary? GateSummary { get; set; }

        // Preview
        public AggieEnterpriseJournalUploadService.PreviewResult? Preview { get; set; }

        public async Task OnGetAsync()
        {
            await LoadPageAsync(loadPreview: false);

            // Only build preview on GET when explicitly requested, and only for the same batch
            if (ShowPreview && SelectedBatchId.HasValue && PreviewForBatchId.HasValue
                && PreviewForBatchId.Value == SelectedBatchId.Value)
            {
                Preview = await _upload.BuildPreviewAsync(SelectedBatchId.Value);
            }
            else
            {
                Preview = null;
            }

            // Consume the flag so it doesn't “stick” across future navigations
            ShowPreview = false;
        }

        public async Task<IActionResult> OnPostBuildPreviewAsync()
        {
            await LoadPageAsync(loadPreview: false);

            if (!SelectedBatchId.HasValue)
            {
                PageMessage = "Please select a batch.";
                return RedirectToThis();
            }

            // PRG: set flags and redirect to GET
            PreviewForBatchId = SelectedBatchId.Value;
            ShowPreview = true;

            PageMessage = "Preview built.";
            return RedirectToThis();
        }

        public async Task<IActionResult> OnPostSendToAeAsync()
        {
            await LoadPageAsync(loadPreview: false);

            if (!SelectedBatchId.HasValue)
            {
                PageMessage = "Please select a batch.";
                return RedirectToThis();
            }

            var result = await _upload.SendBatchAsync(SelectedBatchId.Value);
            PageMessage = result.Message;

            // after sending, do not keep preview
            ShowPreview = false;
            PreviewForBatchId = null;

            return RedirectToThis();
        }

        public async Task<IActionResult> OnPostRefreshStatusAsync()
        {
            await LoadPageAsync(loadPreview: false);

            if (!SelectedBatchId.HasValue)
            {
                PageMessage = "Please select a batch.";
                return RedirectToThis();
            }

            var result = await _upload.CheckStatusAsync(SelectedBatchId.Value);
            PageMessage = result.Message;

            return RedirectToThis();
        }

        public async Task<IActionResult> OnGetDownloadPayloadAsync()
        {
            if (!SelectedBatchId.HasValue)
                return RedirectToThis();

            var json = await _upload.BuildPayloadJsonAsync(SelectedBatchId.Value);
            var fileName = $"glJournalRequest_{SelectedBatchId.Value}_{DateTime.UtcNow:yyyyMMddHHmmss}.json";

            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
        }

        private async Task LoadPageAsync(bool loadPreview)
        {
            ResolveSelectedBatchIdFromRequest();

            // If user changed batch (GET), kill preview flags immediately
            if (!HttpMethods.IsPost(Request.Method))
            {
                if (PreviewForBatchId.HasValue && SelectedBatchId.HasValue &&
                    PreviewForBatchId.Value != SelectedBatchId.Value)
                {
                    ShowPreview = false;
                    PreviewForBatchId = null;
                }
            }

            // Load batches with filters
            var q = _db.FeedBatches.AsQueryable();

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

            if (!string.IsNullOrWhiteSpace(Status))
            {
                var s = Status.Trim().ToUpperInvariant();
                q = q.Where(b => b.AERequestStatus != null && b.AERequestStatus.ToUpper() == s);
            }

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

            // Auto-select first batch ONLY if user did not provide selection in query or form
            var hasQuery = Request.Query.ContainsKey("SelectedBatchId");
            var hasForm = HttpMethods.IsPost(Request.Method) && Request.Form.ContainsKey("SelectedBatchId");

            if (!hasQuery && !hasForm && !SelectedBatchId.HasValue && Batches.Any())
            {
                SelectedBatchId = Batches.First().BatchID;
                ShowPreview = false;
                PreviewForBatchId = null;
            }

            // Load selected batch + gatekeeping
            if (SelectedBatchId.HasValue)
            {
                SelectedBatch = await _db.FeedBatches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BatchID == SelectedBatchId.Value);

                if (SelectedBatch != null)
                {
                    var acctDt = (SelectedBatch.AETransactionDate ?? SelectedBatch.DateSent ?? DateTime.UtcNow).Date;
                    AccountingDateText = acctDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    AccountingPeriodText = acctDt.ToString("MMM-yy", CultureInfo.InvariantCulture);

                    var gate = await _gatekeeper.CanSendBatchAsync(SelectedBatchId.Value);
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
                // not used in this flow, but kept for completeness
                Preview = await _upload.BuildPreviewAsync(SelectedBatchId.Value);
                PreviewForBatchId = SelectedBatchId.Value;
                ShowPreview = true;
            }
        }

        private void ResolveSelectedBatchIdFromRequest()
        {
            // POST
            if (HttpMethods.IsPost(Request.Method))
            {
                var rawForm = Request.Form["SelectedBatchId"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(rawForm) && Guid.TryParse(rawForm, out var g))
                {
                    SelectedBatchId = g;
                    return;
                }
            }

            // GET
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
                FromDate = FromDate?.ToString("yyyy-MM-dd"),
                ToDate = ToDate?.ToString("yyyy-MM-dd"),
                Status,
                JournalName,
                SelectedBatchId
            });
        }
    }
}
