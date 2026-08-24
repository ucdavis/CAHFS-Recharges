using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CAHFS.GraphQl;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Pages.Integrations.Staging
{
    public class CoaValidationsModel : IntegrationPageModel
    {
        private readonly IIntegrationDbResolver _dbResolver;
        private readonly IAggieEnterpriseClient _ae;
        private readonly StarLimsCoaWritebackService _starLimsWriteback;

        public CoaValidationsModel(
            IIntegrationDbResolver dbResolver,
            IAggieEnterpriseClient ae,
            StarLimsCoaWritebackService starLimsWriteback,
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService)
            : base(integrationService, authorizationService)
        {
            _dbResolver = dbResolver;
            _ae = ae;
            _starLimsWriteback = starLimsWriteback;
        }

        private IntegrationType ResolvedIntegration => CurrentIntegration ?? IntegrationType.CAHFS;

        #region Summary Cards

        public int InvalidItemCount { get; set; }
        public int InvalidBatchCount { get; set; }
        public int CorrectedTodayCount { get; set; }

        #endregion

        #region Batch Picker

        [BindProperty(SupportsGet = true)]
        public Guid? SelectedBatchId { get; set; }

        public IList<FeedBatch> InvalidBatches { get; set; } = new List<FeedBatch>();

        #endregion

        #region Item Navigator

        public IList<FeedItem> InvalidItemsInBatch { get; set; } = new List<FeedItem>();

        [BindProperty(SupportsGet = true)]
        public int CurrentItemIndex { get; set; } = 0;

        public FeedItem? CurrentItem { get; set; }
        public int TotalItemsInBatch => InvalidItemsInBatch.Count;
        public bool HasPrevious => CurrentItemIndex > 0;
        public bool HasNext => CurrentItemIndex < TotalItemsInBatch - 1;

        #endregion

        #region Correction Form

        [BindProperty]
        public string? NewDebitCoa { get; set; }

        [BindProperty]
        public string? NewCreditCoa { get; set; }

        [BindProperty]
        public string? CorrectionReason { get; set; }

        // Validation state
        public bool? DebitValidationSuccess { get; set; }
        public string? DebitValidationMessage { get; set; }
        public bool? CreditValidationSuccess { get; set; }
        public string? CreditValidationMessage { get; set; }
        public bool IsValidated { get; set; }

        // Audit history for current item
        public List<CoaCorrectionAudit> ItemAuditHistory { get; set; } = new();

        #endregion

        private object RouteQuery =>
            new { Integration, SelectedBatchId, CurrentItemIndex };

        public async Task OnGetAsync()
        {
            await LoadPageDataAsync();
        }

        public async Task<IActionResult> OnPostValidateAsync()
        {
            if (!await IsOperatorAsync())
            {
                SetErrorMessage("You do not have permission to perform this action.");
                return RedirectToPage(RouteQuery);
            }

            await LoadPageDataAsync();

            if (CurrentItem == null)
            {
                SetErrorMessage("No item selected for validation.");
                return Page();
            }

            if (CurrentItem.DoNotInclude)
            {
                SetErrorMessage("This line is excluded from the batch. Include it before validating or correcting COA.");
                return Page();
            }

            // Validate new Debit COA if provided and different
            if (!string.IsNullOrWhiteSpace(NewDebitCoa))
            {
                var result = await ValidateCoaAsync(NewDebitCoa.Trim());
                DebitValidationSuccess = result.IsValid;
                DebitValidationMessage = result.IsValid ? "Valid" : result.Error ?? "Invalid — check format and try again";
            }

            // Validate new Credit COA if provided and different
            if (!string.IsNullOrWhiteSpace(NewCreditCoa))
            {
                var result = await ValidateCoaAsync(NewCreditCoa.Trim());
                CreditValidationSuccess = result.IsValid;
                CreditValidationMessage = result.IsValid ? "Valid" : result.Error ?? "Invalid — check format and try again";
            }

            // Determine if validation passed
            bool debitOk = string.IsNullOrWhiteSpace(NewDebitCoa) || DebitValidationSuccess == true;
            bool creditOk = string.IsNullOrWhiteSpace(NewCreditCoa) || CreditValidationSuccess == true;
            IsValidated = debitOk && creditOk && (!string.IsNullOrWhiteSpace(NewDebitCoa) || !string.IsNullOrWhiteSpace(NewCreditCoa));

            return Page();
        }

        public async Task<IActionResult> OnPostSubmitAsync()
        {
            if (!await IsOperatorAsync())
            {
                SetErrorMessage("You do not have permission to perform this action.");
                return RedirectToPage(RouteQuery);
            }

            await LoadPageDataAsync();

            if (CurrentItem == null)
            {
                SetErrorMessage("No item selected for correction.");
                return RedirectToPage(RouteQuery);
            }

            if (CurrentItem.DoNotInclude)
            {
                SetErrorMessage("This line is excluded from the batch. Include it before saving a COA correction.");
                return Page();
            }

            // Re-validate before saving
            bool debitOk = true;
            bool creditOk = true;

            if (!string.IsNullOrWhiteSpace(NewDebitCoa) && NewDebitCoa.Trim() != CurrentItem.DebitChartString)
            {
                var result = await ValidateCoaAsync(NewDebitCoa.Trim());
                debitOk = result.IsValid;
                if (!debitOk)
                {
                    DebitValidationSuccess = false;
                    DebitValidationMessage = result.Error ?? "Invalid — check format and try again";
                }
            }

            if (!string.IsNullOrWhiteSpace(NewCreditCoa) && NewCreditCoa.Trim() != CurrentItem.CreditChartString)
            {
                var result = await ValidateCoaAsync(NewCreditCoa.Trim());
                creditOk = result.IsValid;
                if (!creditOk)
                {
                    CreditValidationSuccess = false;
                    CreditValidationMessage = result.Error ?? "Invalid — check format and try again";
                }
            }

            if (!debitOk || !creditOk)
            {
                SetErrorMessage("Validation failed. Please fix the COA values and try again.");
                return Page();
            }

            // Load the tracked entity for update
            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);
            var item = await feedItems.FirstOrDefaultAsync(i => i.RecordID == CurrentItem.RecordID);
            if (item == null)
            {
                SetErrorMessage("Record not found.");
                return RedirectToPage(RouteQuery);
            }

            if (item.DoNotInclude)
            {
                SetErrorMessage("This line is excluded from the batch. Include it before saving a COA correction.");
                return Page();
            }

            var username = User.Identity?.Name ?? "Unknown";
            var now = DateTime.UtcNow;

            // Track changes for audit
            string? oldDebit = null;
            string? newDebit = null;
            string? oldCredit = null;
            string? newCredit = null;
            bool debitChanged = false;
            bool creditChanged = false;

            // Update Debit COA if changed
            if (!string.IsNullOrWhiteSpace(NewDebitCoa) && NewDebitCoa.Trim() != item.DebitChartString)
            {
                oldDebit = item.DebitChartString;
                newDebit = NewDebitCoa.Trim();

                item.DebitChartString = newDebit;
                item.DebitStringValid = "Valid";
                item.DebitValidationError = null;
                debitChanged = true;
            }

            // Update Credit COA if changed
            if (!string.IsNullOrWhiteSpace(NewCreditCoa) && NewCreditCoa.Trim() != item.CreditChartString)
            {
                oldCredit = item.CreditChartString;
                newCredit = NewCreditCoa.Trim();

                item.CreditChartString = newCredit;
                item.CreditStringValid = "Valid";
                item.CreditValidationError = null;
                creditChanged = true;
            }

            if (debitChanged || creditChanged)
            {
                // Create audit record
                var audit = new CoaCorrectionAudit
                {
                    AuditId = Guid.NewGuid(),
                    RecordId = item.RecordID,
                    BatchId = item.BatchID,
                    CorrectionType = debitChanged && creditChanged ? "Both" : (debitChanged ? "Debit" : "Credit"),
                    OldDebitCoa = oldDebit,
                    NewDebitCoa = newDebit,
                    OldCreditCoa = oldCredit,
                    NewCreditCoa = newCredit,
                    CorrectedBy = username,
                    CorrectedAt = now,
                    CorrectionReason = CorrectionReason,
                    IntegrationType = ResolvedIntegration.ToString()
                };

                _dbResolver.GetCoaCorrectionAudits(ResolvedIntegration).Add(audit);
                await _dbResolver.SaveChangesAsync(ResolvedIntegration);

                // Update batch status if all included items are now valid
                await UpdateBatchStatusAsync(item.BatchID);

                SetSuccessMessage($"COA corrected successfully for item.");

                if (ResolvedIntegration == IntegrationType.CAHFS)
                {
                    var chosenNewCoa = debitChanged ? newDebit : newCredit;
                    var chosenOldRaw = debitChanged ? oldDebit : oldCredit;
                    chosenOldRaw ??= item.UnprocessedCOAString;

                    var (ok, msg) = await _starLimsWriteback.TryWritebackAsync(
                        docNumber: item.OrignalDocNumber,
                        newCoa: chosenNewCoa,
                        oldRawChargeNo: chosenOldRaw,
                        ct: HttpContext?.RequestAborted ?? CancellationToken.None);

                    if (!ok)
                        SetInfoMessage(msg);
                }

                // Move to next item if available, otherwise stay
                if (HasNext)
                {
                    return RedirectToPage(new { Integration, SelectedBatchId, CurrentItemIndex = CurrentItemIndex + 1 });
                }
            }
            else
            {
                SetInfoMessage("No changes detected.");
            }

            return RedirectToPage(new { Integration, SelectedBatchId, CurrentItemIndex });
        }

        public IActionResult OnPostReset()
        {
            return RedirectToPage(new { Integration, SelectedBatchId, CurrentItemIndex });
        }

        public async Task<IActionResult> OnPostToggleIncludeAsync(Guid recordId)
        {
            if (!await IsOperatorAsync())
            {
                SetErrorMessage("You do not have permission to perform this action.");
                return RedirectToPage(RouteQuery);
            }

            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);
            var item = await feedItems.FirstOrDefaultAsync(i => i.RecordID == recordId);
            if (item == null)
            {
                SetErrorMessage("Record not found.");
                return RedirectToPage(RouteQuery);
            }

            item.DoNotInclude = !item.DoNotInclude;
            await _dbResolver.SaveChangesAsync(ResolvedIntegration);
            await UpdateBatchStatusAsync(item.BatchID);

            SetSuccessMessage(item.DoNotInclude
                ? "Item excluded from this batch (not sent to AE, not counted in validation)."
                : "Item included in this batch again.");

            return RedirectToPage(new { Integration, SelectedBatchId = item.BatchID, CurrentItemIndex });
        }

        private async Task LoadPageDataAsync()
        {
            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);
            var feedBatches = _dbResolver.GetFeedBatches(ResolvedIntegration);
            var coaAudits = _dbResolver.GetCoaCorrectionAudits(ResolvedIntegration);

            // Summary: issue item count (included rows only; COA not fully Valid)
            InvalidItemCount = await feedItems
                .Where(i => !i.DoNotInclude &&
                    ((i.DebitStringValid ?? "") != "Valid" || (i.CreditStringValid ?? "") != "Valid"))
                .CountAsync();

            // Summary: distinct batches with issue items (included only)
            var invalidBatchIds = await feedItems
                .Where(i => !i.DoNotInclude &&
                    ((i.DebitStringValid ?? "") != "Valid" || (i.CreditStringValid ?? "") != "Valid"))
                .Select(i => i.BatchID)
                .Distinct()
                .ToListAsync();

            InvalidBatchCount = invalidBatchIds.Count;

            // Summary: Corrected today (count from audit table)
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);
            try
            {
                CorrectedTodayCount = await coaAudits
                    .Where(a => a.CorrectedAt >= todayStart && a.CorrectedAt < todayEnd)
                    .CountAsync();
            }
            catch
            {
                CorrectedTodayCount = 0;
            }

            if (invalidBatchIds.Any())
            {
                var invalidBatchIdSet = new HashSet<Guid>(invalidBatchIds);

                var candidateBatches = await feedBatches
                    .Where(b => b.AERequestStatus == "Needs Review" || b.AERequestStatus == "Error" || b.AERequestStatus == null)
                    .OrderByDescending(b => b.AETransactionDate ?? b.DateSent)
                    .Take(500)
                    .AsNoTracking()
                    .ToListAsync();

                InvalidBatches = candidateBatches
                    .Where(b => invalidBatchIdSet.Contains(b.BatchID))
                    .Take(100)
                    .ToList();
            }

            // Default to first batch if none selected OR if selected batch is not in the list
            bool selectedBatchInList = SelectedBatchId.HasValue && InvalidBatches.Any(b => b.BatchID == SelectedBatchId.Value);
            if ((!SelectedBatchId.HasValue || !selectedBatchInList) && InvalidBatches.Any())
            {
                SelectedBatchId = InvalidBatches.First().BatchID;
            }

            // Load items for selected batch
            if (SelectedBatchId.HasValue)
            {
                // Lines with COA issues (included) plus excluded lines (so operators can re-include)
                var q = feedItems.Where(i => i.BatchID == SelectedBatchId.Value &&
                    (i.DoNotInclude ||
                     ((i.DebitStringValid ?? "") != "Valid" || (i.CreditStringValid ?? "") != "Valid")));

                InvalidItemsInBatch = await q
                    .OrderBy(i => i.TransactionDate)
                    .ThenBy(i => i.RecordID)
                    .AsNoTracking()
                    .ToListAsync();

                // Clamp index
                if (CurrentItemIndex >= InvalidItemsInBatch.Count)
                    CurrentItemIndex = Math.Max(0, InvalidItemsInBatch.Count - 1);

                // Set current item
                if (InvalidItemsInBatch.Any() && CurrentItemIndex >= 0 && CurrentItemIndex < InvalidItemsInBatch.Count)
                {
                    CurrentItem = InvalidItemsInBatch[CurrentItemIndex];

                    try
                    {
                        ItemAuditHistory = await coaAudits
                            .Where(a => a.RecordId == CurrentItem.RecordID)
                            .OrderByDescending(a => a.CorrectedAt)
                            .Take(10)
                            .AsNoTracking()
                            .ToListAsync();
                    }
                    catch
                    {
                        ItemAuditHistory = new List<CoaCorrectionAudit>();
                    }
                }
            }
        }

        private async Task<(bool IsValid, string? Error)> ValidateCoaAsync(string coa)
        {
            if (string.IsNullOrWhiteSpace(coa))
                return (false, "COA is empty");

            try
            {
                var resp = await _ae.ErpValidateChartstring.ExecuteAsync(
                    segmentString: coa,
                    validateCVRs: true,
                    accountingDate: null
                );

                if (resp.Errors?.Any() == true)
                {
                    var msg = string.Join(" | ", resp.Errors.Select(e => e.Message));
                    return (false, msg);
                }

                var vr = resp.Data?.ErpValidateChartstring?.ValidationResponse;
                var valid = vr?.Valid ?? false;

                var err = (vr?.ErrorMessages is { Count: > 0 })
                    ? string.Join(" | ", vr.ErrorMessages)
                    : null;

                return (valid, err);
            }
            catch (Exception ex)
            {
                return (false, $"AE API error: {ex.Message}");
            }
        }

        private async Task UpdateBatchStatusAsync(Guid batchId)
        {
            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);

            // Keep Send-to-AE Batch Total / dropdown in sync with included lines only
            var batchTotal = await feedItems
                .Where(i => i.BatchID == batchId && !i.DoNotInclude)
                .SumAsync(i => (decimal?)i.TotalCharge) ?? 0m;

            await _dbResolver.ExecuteSqlAsync(ResolvedIntegration,
                $"UPDATE C_AE_Feed_Batch SET batchTotal = {batchTotal} WHERE batchID = {batchId}");

            // Match StagingCoaValidationService / FeedReview include: Ready ↔ Needs Review
            var hasCoaIssue = await feedItems.AnyAsync(i =>
                i.BatchID == batchId &&
                !i.DoNotInclude &&
                i.DebitStringValid != null &&
                i.CreditStringValid != null &&
                (i.DebitStringValid != "Valid" || i.CreditStringValid != "Valid"));

            if (hasCoaIssue)
            {
                await _dbResolver.ExecuteSqlAsync(ResolvedIntegration,
                    $"UPDATE C_AE_Feed_Batch SET AERequestStatus = {"Needs Review"} WHERE batchID = {batchId}");
                return;
            }

            var hasPending = await feedItems.AnyAsync(i =>
                i.BatchID == batchId &&
                !i.DoNotInclude &&
                (i.DebitStringValid == null || i.CreditStringValid == null));

            if (!hasPending)
            {
                await _dbResolver.ExecuteSqlAsync(ResolvedIntegration,
                    $"UPDATE C_AE_Feed_Batch SET AERequestStatus = {"Ready"} WHERE batchID = {batchId}");
            }
        }
    }
}
