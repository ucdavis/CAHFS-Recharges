using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Pages.Integrations.History
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class SentHistoryModel : IntegrationPageModel
    {
        private readonly IIntegrationDbResolver _dbResolver;

        public SentHistoryModel(
            IIntegrationDbResolver dbResolver,
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService)
            : base(integrationService, authorizationService)
        {
            _dbResolver = dbResolver;
        }

        private IntegrationType ResolvedIntegration => CurrentIntegration ?? IntegrationType.CAHFS;

        #region Filters

        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? StatusFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? JournalFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public Guid? BatchIdFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public Guid? RequestIdFilter { get; set; }

        public List<string> AvailableStatuses { get; set; } = new() { "All", "Sent", "Completed", "Error", "Pending" };

        #endregion

        #region Results

        public List<SentBatchViewModel> SentBatches { get; set; } = new();
        public int TotalCount { get; set; }

        #endregion

        #region Detail View

        [BindProperty(SupportsGet = true)]
        public Guid? ExpandedBatchId { get; set; }

        public SentBatchDetailViewModel? ExpandedBatchDetail { get; set; }

        #endregion

        #region Items Preview

        [BindProperty(SupportsGet = true)]
        public Guid? PreviewBatchId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? PreviewSearch { get; set; }

        public IList<FeedItem> PreviewItems { get; set; } = new List<FeedItem>();

        #endregion

        public async Task OnGetAsync()
        {
            // No default date range - show all Validated and Error batches by default
            await LoadDataAsync();
        }

        public async Task<IActionResult> OnGetDetailAsync(Guid batchId)
        {
            // Load detail for expanded batch
            ExpandedBatchId = batchId;
            await LoadDataAsync();
            return Page();
        }

        private async Task LoadDataAsync()
        {
            var feedBatches = _dbResolver.GetFeedBatches(ResolvedIntegration);
            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);

            // Query batches with AERequestStatus of "Validated" or "Error" (trim to handle spaces)
            var query = feedBatches
                .Where(b => b.AERequestStatus != null && 
                           (b.AERequestStatus.Trim() == "Validated" || b.AERequestStatus.Trim() == "Error"))
                .AsQueryable();

            // Apply date range filter (use AETransactionDate or DateSent, whichever is available)
            if (StartDate.HasValue)
                query = query.Where(b => (b.AETransactionDate != null && b.AETransactionDate >= StartDate.Value) ||
                                         (b.AETransactionDate == null && b.DateSent != null && b.DateSent >= StartDate.Value));

            if (EndDate.HasValue)
                query = query.Where(b => (b.AETransactionDate != null && b.AETransactionDate <= EndDate.Value.AddDays(1)) ||
                                         (b.AETransactionDate == null && b.DateSent != null && b.DateSent <= EndDate.Value.AddDays(1)));

            // Apply status filter (use explicit OR conditions to avoid SQL Contains() issues)
            if (!string.IsNullOrEmpty(StatusFilter) && StatusFilter != "All")
            {
                query = StatusFilter switch
                {
                    "Completed" => query.Where(b => b.AERequestStatus == "Complete" || 
                                                    b.AERequestStatus == "Completed" || 
                                                    b.AERequestStatus == "Success"),
                    "Sent" => query.Where(b => b.AERequestStatus == "Sent" || 
                                               b.AERequestStatus == "Submitted" || 
                                               b.AERequestStatus == "Validated" ||
                                               b.AERequestStatus == "Processing"),
                    "Pending" => query.Where(b => b.AERequestStatus == "Pending" || 
                                                  b.AERequestStatus == "Needs Review" || 
                                                  b.AERequestStatus == "Ready" ||
                                                  b.AERequestStatus == null),
                    "Error" => query.Where(b => b.AERequestStatus == "Error" || 
                                                b.AERequestStatus == "Failed" || 
                                                b.AERequestStatus == "Rejected"),
                    _ => query
                };
            }

            // Apply journal filter
            if (!string.IsNullOrEmpty(JournalFilter))
                query = query.Where(b => b.AEJournalName != null && b.AEJournalName.Contains(JournalFilter));

            // Apply batch ID filter
            if (BatchIdFilter.HasValue)
                query = query.Where(b => b.BatchID == BatchIdFilter.Value);

            // Apply request ID filter
            if (RequestIdFilter.HasValue)
                query = query.Where(b => b.AEConsumerRequestID == RequestIdFilter.Value);

            // Get batches
            var batches = await query
                .OrderByDescending(b => b.DateSent)
                .Take(500)
                .AsNoTracking()
                .ToListAsync();

            // Get item counts for each batch using in-memory filtering to avoid SQL issues
            var batchIdSet = new HashSet<Guid>(batches.Select(b => b.BatchID));
            var itemCountDict = new Dictionary<Guid, int>();

            if (batchIdSet.Any())
            {
                // Query all items and filter in memory to avoid SQL Contains() issues
                var allItemBatchIds = await feedItems
                    .Select(i => i.BatchID)
                    .ToListAsync();

                itemCountDict = allItemBatchIds
                    .Where(id => batchIdSet.Contains(id))
                    .GroupBy(id => id)
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            // Map to view models
            SentBatches = batches.Select(b => new SentBatchViewModel
            {
                BatchId = b.BatchID,
                JournalName = b.AEJournalName,
                JournalDescription = b.AEJournalDescription,
                Period = b.AETransactionDate?.ToString("MMM yyyy"),
                BatchTotal = b.BatchTotal,
                SentAt = b.DateSent,
                RequestId = b.AEConsumerRequestID,
                Status = MapDbStatusToDisplayStatus(b.AERequestStatus),
                ItemCount = itemCountDict.GetValueOrDefault(b.BatchID, 0),
                ErrorDetail = b.ErrorDetail,
                SentBy = null // Database doesn't have SentBy field yet
            }).ToList();

            TotalCount = SentBatches.Count;

            // Load expanded batch detail if requested
            if (ExpandedBatchId.HasValue)
            {
                var batch = SentBatches.FirstOrDefault(b => b.BatchId == ExpandedBatchId.Value);
                if (batch != null)
                {
                    // Get items with errors for this batch (fetch data first, then map in memory)
                    var errorItemsRaw = await feedItems
                        .Where(i => i.BatchID == ExpandedBatchId.Value &&
                                    (i.DebitStringValid == "Invalid" || i.CreditStringValid == "Invalid"))
                        .Select(i => new
                        {
                            i.RecordID,
                            i.TestCode,
                            i.TestName,
                            i.DebitValidationError,
                            i.CreditValidationError
                        })
                        .Take(50)
                        .AsNoTracking()
                        .ToListAsync();

                    // Map to view models in memory (string.Join can't be translated to SQL)
                    var itemErrors = errorItemsRaw.Select(i => new ItemErrorViewModel
                    {
                        RecordId = i.RecordID,
                        Description = $"{i.TestCode} - {i.TestName}",
                        ErrorMessage = string.Join(" | ",
                            new[] { i.DebitValidationError, i.CreditValidationError }
                                .Where(e => !string.IsNullOrEmpty(e)))
                    }).ToList();

                    ExpandedBatchDetail = new SentBatchDetailViewModel
                    {
                        BatchId = batch.BatchId,
                        JournalName = batch.JournalName,
                        Status = batch.Status,
                        SentAt = batch.SentAt,
                        RequestId = batch.RequestId,
                        ErrorDetail = batch.ErrorDetail,
                        AeResponseText = batch.ErrorDetail ?? "(No response stored)",
                        ItemErrors = itemErrors
                    };
                }
            }

            // Load full items preview if requested
            if (PreviewBatchId.HasValue)
            {
                var previewQuery = feedItems.Where(i => i.BatchID == PreviewBatchId.Value);

                if (!string.IsNullOrWhiteSpace(PreviewSearch))
                {
                    var term = PreviewSearch.Trim();

                    // Typed matches (avoid EF-unsupported ToString() on DateTime/numerics)
                    Guid? recordIdTerm = null;
                    if (Guid.TryParse(term, out var recordGuid))
                        recordIdTerm = recordGuid;

                    int? intTerm = null;
                    if (int.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iVal))
                        intTerm = iVal;

                    decimal? decimalTerm = null;
                    if (decimal.TryParse(term, NumberStyles.Number, CultureInfo.InvariantCulture, out var dVal))
                        decimalTerm = dVal;

                    DateTime? dateTerm = null;
                    if (DateTime.TryParseExact(term, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtExact))
                        dateTerm = dtExact.Date;

                    int? yearTerm = null;
                    int? monthTerm = null;
                    if (term.Length == 7 && term[4] == '-')
                    {
                        if (int.TryParse(term.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
                            int.TryParse(term.Substring(5, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
                        {
                            yearTerm = y;
                            monthTerm = m;
                        }
                    }
                    else if (term.Length == 4 && int.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yOnly))
                    {
                        yearTerm = yOnly;
                    }

                    previewQuery = previewQuery.Where(i =>
                        // IDs and basic fields
                        (recordIdTerm.HasValue && i.RecordID == recordIdTerm.Value) ||
                        (i.OrignalDocNumber != null && i.OrignalDocNumber.Contains(term)) ||
                        (i.ClientID != null && i.ClientID.Contains(term)) ||
                        (intTerm.HasValue && i.SystemID == intTerm.Value) ||

                        // Dates and numeric values
                        (dateTerm.HasValue && i.TransactionDate.Date == dateTerm.Value) ||
                        (yearTerm.HasValue && monthTerm.HasValue && i.TransactionDate.Year == yearTerm.Value && i.TransactionDate.Month == monthTerm.Value) ||
                        (yearTerm.HasValue && !monthTerm.HasValue && i.TransactionDate.Year == yearTerm.Value) ||
                        (intTerm.HasValue && i.Quantity == intTerm.Value) ||
                        (decimalTerm.HasValue && i.UnitPrice == decimalTerm.Value) ||
                        (decimalTerm.HasValue && i.TotalCharge == decimalTerm.Value) ||

                        // COA strings and validity
                        (i.DebitChartString != null && i.DebitChartString.Contains(term)) ||
                        (i.CreditChartString != null && i.CreditChartString.Contains(term)) ||
                        (i.DebitStringValid != null && i.DebitStringValid.Contains(term)) ||
                        (i.CreditStringValid != null && i.CreditStringValid.Contains(term)) ||

                        // Test metadata
                        (i.TestCode != null && i.TestCode.Contains(term)) ||
                        (i.TestName != null && i.TestName.Contains(term)) ||

                        // COA source and AE error details
                        (i.UnprocessedCOAString != null && i.UnprocessedCOAString.Contains(term)) ||
                        (i.DebitValidationError != null && i.DebitValidationError.Contains(term)) ||
                        (i.CreditValidationError != null && i.CreditValidationError.Contains(term))
                    );
                }

                PreviewItems = await previewQuery
                    .OrderBy(i => i.TransactionDate)
                    .ThenBy(i => i.RecordID)
                    .AsNoTracking()
                    .ToListAsync();
            }
            else
            {
                PreviewItems = new List<FeedItem>();
            }
        }

        /// <summary>
        /// Maps display status to possible AERequestStatus values in the database.
        /// </summary>
        private static List<string> MapDisplayStatusToDbStatuses(string displayStatus)
        {
            return displayStatus switch
            {
                "Completed" => new List<string> { "Complete", "Completed", "Success" },
                "Sent" => new List<string> { "Sent", "Submitted", "Processing" },
                "Pending" => new List<string> { "Pending", "Needs Review", "Ready" },
                "Error" => new List<string> { "Error", "Failed", "Rejected" },
                _ => new List<string>()
            };
        }

        /// <summary>
        /// Maps database AERequestStatus to a display-friendly status.
        /// </summary>
        private static string MapDbStatusToDisplayStatus(string? dbStatus)
        {
            if (string.IsNullOrEmpty(dbStatus))
                return "Pending";

            return dbStatus.ToLowerInvariant() switch
            {
                "complete" or "completed" or "success" => "Completed",
                "sent" or "submitted" or "processing" => "Sent",
                "pending" or "needs review" or "ready" => "Pending",
                "error" or "failed" or "rejected" => "Error",
                _ => dbStatus
            };
        }

        public string GetDebugSummary(SentBatchViewModel batch)
        {
            return $@"=== CAEI Debug Summary ===
Integration: {ResolvedIntegration}
BatchId: {batch.BatchId}
RequestId: {batch.RequestId}
Journal: {batch.JournalName}
Status: {batch.Status}
SentAt: {batch.SentAt:yyyy-MM-dd HH:mm:ss}
Total: {batch.BatchTotal:C}
Error: {batch.ErrorDetail ?? "(none)"}
========================";
        }

        /// <summary>
        /// Downloads the items for the selected batch (sent to AE) as Excel.
        /// </summary>
        public async Task<IActionResult> OnGetDownloadPayloadAsync(Guid batchId)
        {
            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);
            var items = await feedItems
                .Where(i => i.BatchID == batchId)
                .OrderBy(i => i.TransactionDate)
                .ThenBy(i => i.RecordID)
                .AsNoTracking()
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Batch Items");

            int row = 1;
            ws.Cell(row, 1).Value = "RecordID";
            ws.Cell(row, 2).Value = "BatchID";
            ws.Cell(row, 3).Value = "OrignalDocNumber";
            ws.Cell(row, 4).Value = "TransactionDate";
            ws.Cell(row, 5).Value = "TestCode";
            ws.Cell(row, 6).Value = "TestName";
            ws.Cell(row, 7).Value = "Quantity";
            ws.Cell(row, 8).Value = "UnitPrice";
            ws.Cell(row, 9).Value = "TotalCharge";
            ws.Cell(row, 10).Value = "DebitChartString";
            ws.Cell(row, 11).Value = "CreditChartString";
            ws.Cell(row, 12).Value = "DebitStringValid";
            ws.Cell(row, 13).Value = "CreditStringValid";
            ws.Cell(row, 14).Value = "DebitValidationError";
            ws.Cell(row, 15).Value = "CreditValidationError";
            ws.Cell(row, 16).Value = "ClientID";

            var headerRange = ws.Range(row, 1, row, 16);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            row++;

            foreach (var i in items)
            {
                ws.Cell(row, 1).Value = i.RecordID.ToString();
                ws.Cell(row, 2).Value = i.BatchID.ToString();
                ws.Cell(row, 3).Value = i.OrignalDocNumber;
                ws.Cell(row, 4).Value = i.TransactionDate;
                ws.Cell(row, 5).Value = i.TestCode;
                ws.Cell(row, 6).Value = i.TestName;
                ws.Cell(row, 7).Value = i.Quantity;
                ws.Cell(row, 8).Value = i.UnitPrice;
                ws.Cell(row, 9).Value = i.TotalCharge;
                ws.Cell(row, 10).Value = i.DebitChartString;
                ws.Cell(row, 11).Value = i.CreditChartString;
                ws.Cell(row, 12).Value = i.DebitStringValid;
                ws.Cell(row, 13).Value = i.CreditStringValid;
                ws.Cell(row, 14).Value = i.DebitValidationError;
                ws.Cell(row, 15).Value = i.CreditValidationError;
                ws.Cell(row, 16).Value = i.ClientID;
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"BatchItems_{batchId:N}.xlsx";
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            var feedBatches = _dbResolver.GetFeedBatches(ResolvedIntegration);

            // Build query with same filters as page display (trim to handle spaces)
            var query = feedBatches
                .Where(b => b.AERequestStatus != null && 
                           (b.AERequestStatus.Trim() == "Validated" || b.AERequestStatus.Trim() == "Error"))
                .AsQueryable();

            // Apply date range filter (use AETransactionDate or DateSent)
            if (StartDate.HasValue)
                query = query.Where(b => (b.AETransactionDate != null && b.AETransactionDate >= StartDate.Value) ||
                                         (b.AETransactionDate == null && b.DateSent != null && b.DateSent >= StartDate.Value));

            if (EndDate.HasValue)
                query = query.Where(b => (b.AETransactionDate != null && b.AETransactionDate <= EndDate.Value.AddDays(1)) ||
                                         (b.AETransactionDate == null && b.DateSent != null && b.DateSent <= EndDate.Value.AddDays(1)));

            if (!string.IsNullOrEmpty(StatusFilter) && StatusFilter != "All")
            {
                query = StatusFilter switch
                {
                    "Completed" => query.Where(b => b.AERequestStatus == "Complete" || 
                                                    b.AERequestStatus == "Completed" || 
                                                    b.AERequestStatus == "Success"),
                    "Sent" => query.Where(b => b.AERequestStatus == "Sent" || 
                                               b.AERequestStatus == "Submitted" || 
                                               b.AERequestStatus == "Validated" ||
                                               b.AERequestStatus == "Processing"),
                    "Pending" => query.Where(b => b.AERequestStatus == "Pending" || 
                                                  b.AERequestStatus == "Needs Review" || 
                                                  b.AERequestStatus == "Ready" ||
                                                  b.AERequestStatus == null),
                    "Error" => query.Where(b => b.AERequestStatus == "Error" || 
                                                b.AERequestStatus == "Failed" || 
                                                b.AERequestStatus == "Rejected"),
                    _ => query
                };
            }

            if (!string.IsNullOrEmpty(JournalFilter))
                query = query.Where(b => b.AEJournalName != null && b.AEJournalName.Contains(JournalFilter));

            if (BatchIdFilter.HasValue)
                query = query.Where(b => b.BatchID == BatchIdFilter.Value);

            if (RequestIdFilter.HasValue)
                query = query.Where(b => b.AEConsumerRequestID == RequestIdFilter.Value);

            var batches = await query
                .OrderByDescending(b => b.DateSent)
                .Take(1000)
                .AsNoTracking()
                .ToListAsync();

            // Create Excel workbook
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Sent History");

            int row = 1;

            // Header row
            ws.Cell(row, 1).Value = "BatchID";
            ws.Cell(row, 2).Value = "JournalName";
            ws.Cell(row, 3).Value = "JournalDescription";
            ws.Cell(row, 4).Value = "Period";
            ws.Cell(row, 5).Value = "BatchTotal";
            ws.Cell(row, 6).Value = "DateSent";
            ws.Cell(row, 7).Value = "RequestID";
            ws.Cell(row, 8).Value = "Status";
            ws.Cell(row, 9).Value = "ErrorDetail";

            // Style header
            var headerRange = ws.Range(row, 1, row, 9);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            row++;

            // Data rows
            foreach (var b in batches)
            {
                ws.Cell(row, 1).Value = b.BatchID.ToString();
                ws.Cell(row, 2).Value = b.AEJournalName;
                ws.Cell(row, 3).Value = b.AEJournalDescription;
                ws.Cell(row, 4).Value = b.AETransactionDate?.ToString("MMM yyyy");
                ws.Cell(row, 5).Value = b.BatchTotal;
                ws.Cell(row, 6).Value = b.DateSent;
                ws.Cell(row, 7).Value = b.AEConsumerRequestID?.ToString() ?? "";
                ws.Cell(row, 8).Value = MapDbStatusToDisplayStatus(b.AERequestStatus);
                ws.Cell(row, 9).Value = b.ErrorDetail;

                row++;
            }

            // Auto-fit columns
            ws.Columns().AdjustToContents();

            // Generate file
            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"SentHistory_{ResolvedIntegration}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
    }

    #region View Models

    public class SentBatchViewModel
    {
        public Guid BatchId { get; set; }
        public string? JournalName { get; set; }
        public string? JournalDescription { get; set; }
        public string? Period { get; set; }
        public decimal? BatchTotal { get; set; }
        public DateTime? SentAt { get; set; }
        public Guid? RequestId { get; set; }
        public string? Status { get; set; }
        public int ItemCount { get; set; }
        public string? ErrorDetail { get; set; }
        public string? SentBy { get; set; }
    }

    public class SentBatchDetailViewModel
    {
        public Guid BatchId { get; set; }
        public string? JournalName { get; set; }
        public string? Status { get; set; }
        public DateTime? SentAt { get; set; }
        public Guid? RequestId { get; set; }
        public string? ErrorDetail { get; set; }
        public string? AeResponseText { get; set; }
        public List<ItemErrorViewModel> ItemErrors { get; set; } = new();
    }

    public class ItemErrorViewModel
    {
        public Guid RecordId { get; set; }
        public string? Description { get; set; }
        public string? ErrorMessage { get; set; }
    }

    #endregion
}
