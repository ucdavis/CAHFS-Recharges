using ClosedXML.Excel;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CAHFS_Recharges.Services;
using CAHFS.GraphQl;
using Microsoft.AspNetCore.Authorization;

namespace CAHFS_Recharges.Pages.Integrations.Staging
{
    public class FeedReviewModel : IntegrationPageModel
    {
        private readonly IIntegrationDbResolver _dbResolver;
        private readonly StagingCoaValidationService _coaValidator;
        private readonly IAggieEnterpriseClient _ae;

        public FeedReviewModel(
            IIntegrationDbResolver dbResolver,
            StagingCoaValidationService coaValidator,
            IAggieEnterpriseClient ae,
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService)
            : base(integrationService, authorizationService)
        {
            _dbResolver = dbResolver;
            _coaValidator = coaValidator;
            _ae = ae;
        }

        private IntegrationType ResolvedIntegration => CurrentIntegration ?? IntegrationType.CAHFS;

        // AE Details - querystring
        [BindProperty(SupportsGet = true)]
        public Guid? DetailRecordId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DetailSide { get; set; } // "D" or "C"

        public IGlValidateChartstringResult? AeDetailData { get; set; }
        public string? AeDetailError { get; set; }
        public string? AeDetailInputCoa { get; set; }

        // =======================
        // Filters - querystring
        // =======================
        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? JournalName { get; set; }

        // Selected batch - querystring
        [BindProperty(SupportsGet = true)]
        public Guid? SelectedBatchId { get; set; }

        // Show only invalid items for selected batch (Debit OR Credit)
        [BindProperty(SupportsGet = true)]
        public bool ShowInvalidOnly { get; set; } = false;

        public string? SelectedBatchJournalName { get; set; }

        public IList<FeedBatch> Batches { get; set; } = new List<FeedBatch>();
        public IList<FeedItem> Items { get; set; } = new List<FeedItem>();
        /// <summary>Count of items with Invalid debit or credit COA across filtered batches (for summary card).</summary>
        public int InvalidItemsCount { get; set; }

        // ============================================================
        // POST: Validate pending COA
        // ============================================================
        public async Task<IActionResult> OnPostValidatePendingAsync()
        {
            var updated = await _coaValidator.ValidatePendingItemsAsync(ResolvedIntegration);
            SetSuccessMessage($"COA validation completed. Rows updated: {updated}");

            return RedirectToPage(new
            {
                Integration,
                FromDate = FromDate?.ToString("yyyy-MM-dd"),
                ToDate = ToDate?.ToString("yyyy-MM-dd"),
                Status,
                JournalName,
                SelectedBatchId
            });
        }

        // ============================================================
        // GET: Download Items Excel
        // ============================================================
        public async Task<IActionResult> OnGetDownloadItemsAsync()
        {
            if (!SelectedBatchId.HasValue)
                return RedirectToPage(new { Integration });

            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);
            var q = feedItems.Where(i => i.BatchID == SelectedBatchId.Value);

            if (ShowInvalidOnly)
            {
                q = q.Where(i => i.DebitStringValid == "Invalid" || i.CreditStringValid == "Invalid");
            }

            var items = await q
                .OrderBy(i => i.TransactionDate)
                .ThenBy(i => i.RecordID)
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Items");

            int row = 1;

            ws.Cell(row, 1).Value = "RecordID";
            ws.Cell(row, 2).Value = "OriginalDocNumber";
            ws.Cell(row, 3).Value = "DebitChartString";
            ws.Cell(row, 4).Value = "CreditChartString";
            ws.Cell(row, 5).Value = "TransactionDate";
            ws.Cell(row, 6).Value = "Quantity";
            ws.Cell(row, 7).Value = "UnitPrice";
            ws.Cell(row, 8).Value = "TotalCharge";
            ws.Cell(row, 9).Value = "ClientID";
            ws.Cell(row, 10).Value = "SystemID";
            ws.Cell(row, 11).Value = "DebitStringValid";
            ws.Cell(row, 12).Value = "CreditStringValid";
            ws.Cell(row, 13).Value = "TestCode";
            ws.Cell(row, 14).Value = "TestName";
            ws.Cell(row, 15).Value = "UnprocessedCOAString";
            ws.Cell(row, 16).Value = "AE_Details";
            row++;

            foreach (var i in items)
            {
                var aeDetails =
                    $"D: {(string.IsNullOrWhiteSpace(i.DebitValidationError) ? "-" : i.DebitValidationError)} | " +
                    $"C: {(string.IsNullOrWhiteSpace(i.CreditValidationError) ? "-" : i.CreditValidationError)}";

                ws.Cell(row, 1).Value = i.RecordID.ToString();
                ws.Cell(row, 2).Value = i.OrignalDocNumber;
                ws.Cell(row, 3).Value = i.DebitChartString;
                ws.Cell(row, 4).Value = i.CreditChartString;
                ws.Cell(row, 5).Value = i.TransactionDate;
                ws.Cell(row, 6).Value = i.Quantity;
                ws.Cell(row, 7).Value = i.UnitPrice;
                ws.Cell(row, 8).Value = i.TotalCharge;
                ws.Cell(row, 9).Value = i.ClientID;
                ws.Cell(row, 10).Value = i.SystemID;
                ws.Cell(row, 11).Value = i.DebitStringValid;
                ws.Cell(row, 12).Value = i.CreditStringValid;
                ws.Cell(row, 13).Value = i.TestCode;
                ws.Cell(row, 14).Value = i.TestName;
                ws.Cell(row, 15).Value = i.UnprocessedCOAString ?? "";
                ws.Cell(row, 16).Value = aeDetails;

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            var suffix = ShowInvalidOnly ? "_INVALID_ONLY" : "";
            var fileName = $"AE_Feed_Items_{ResolvedIntegration}_{SelectedBatchId.Value}{suffix}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // ============================================================
        // GET: Download Batches Excel
        // ============================================================
        public async Task<IActionResult> OnGetDownloadBatchesAsync()
        {
            var feedBatches = _dbResolver.GetFeedBatches(ResolvedIntegration);
            var query = feedBatches.AsQueryable();

            if (FromDate.HasValue)
            {
                var from = FromDate.Value.Date;
                query = query.Where(b => (b.AETransactionDate ?? b.DateSent) >= from);
            }

            if (ToDate.HasValue)
            {
                var to = ToDate.Value.Date.AddDays(1);
                query = query.Where(b => (b.AETransactionDate ?? b.DateSent) < to);
            }

            if (!string.IsNullOrWhiteSpace(Status))
            {
                var s = Status.Trim().ToUpper();
                query = query.Where(b => b.AERequestStatus != null &&
                                         b.AERequestStatus.ToUpper() == s);
            }

            if (!string.IsNullOrWhiteSpace(JournalName))
            {
                var j = JournalName.Trim();
                query = query.Where(b => b.AEJournalName.Contains(j));
            }

            var batches = await query
                .OrderByDescending(b => b.AETransactionDate ?? b.DateSent)
                .ThenByDescending(b => b.BatchID)
                .Take(200)
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Batches");

            int row = 1;

            ws.Cell(row, 1).Value = "BatchID";
            ws.Cell(row, 2).Value = "DateSent";
            ws.Cell(row, 3).Value = "PickupSentFlag";
            ws.Cell(row, 4).Value = "AEConsumerReferenceID";
            ws.Cell(row, 5).Value = "AEConsumerNotes";
            ws.Cell(row, 6).Value = "AERequestStatus";
            ws.Cell(row, 7).Value = "ErrorDetail";
            ws.Cell(row, 8).Value = "AEConsumerRequestID";
            ws.Cell(row, 9).Value = "AETransactionDate";
            ws.Cell(row, 10).Value = "AEJournalName";
            ws.Cell(row, 11).Value = "AEJournalDescription";
            ws.Cell(row, 12).Value = "AEJournalReference";
            ws.Cell(row, 13).Value = "BatchTotal";

            row++;

            foreach (var b in batches)
            {
                ws.Cell(row, 1).Value = b.BatchID.ToString();
                ws.Cell(row, 2).Value = b.DateSent;
                ws.Cell(row, 3).Value = b.PickupSentFlag;
                ws.Cell(row, 4).Value = b.AEConsumerReferenceID;
                ws.Cell(row, 5).Value = b.AEConsumnerNotes;
                ws.Cell(row, 6).Value = b.AERequestStatus;
                ws.Cell(row, 7).Value = b.ErrorDetail;
                ws.Cell(row, 8).Value = b.AEConsumerRequestID?.ToString() ?? "";
                ws.Cell(row, 9).Value = b.AETransactionDate;
                ws.Cell(row, 10).Value = b.AEJournalName;
                ws.Cell(row, 11).Value = b.AEJournalDescription;
                ws.Cell(row, 12).Value = b.AEJournalReference;
                ws.Cell(row, 13).Value = b.BatchTotal;

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"AE_Feed_Batch_{ResolvedIntegration}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // ============================================================
        // GET: Download All = ZIP with Batches.xlsx + Items.xlsx (same filters)
        // ============================================================
        public async Task<IActionResult> OnGetDownloadAllAsync()
        {
            var feedBatches = _dbResolver.GetFeedBatches(ResolvedIntegration);
            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);
            var batchQuery = feedBatches.AsQueryable();

            if (FromDate.HasValue)
                batchQuery = batchQuery.Where(b => (b.AETransactionDate ?? b.DateSent) >= FromDate.Value.Date);
            if (ToDate.HasValue)
                batchQuery = batchQuery.Where(b => (b.AETransactionDate ?? b.DateSent) < ToDate.Value.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(Status))
            {
                var s = Status.Trim().ToUpper();
                batchQuery = batchQuery.Where(b => b.AERequestStatus != null && b.AERequestStatus.ToUpper() == s);
            }
            if (!string.IsNullOrWhiteSpace(JournalName))
                batchQuery = batchQuery.Where(b => b.AEJournalName.Contains(JournalName.Trim()));

            var batches = await batchQuery
                .OrderByDescending(b => b.AETransactionDate ?? b.DateSent)
                .ThenByDescending(b => b.BatchID)
                .Take(200)
                .ToListAsync();
            if (batches.Count == 0)
                return RedirectToPage(new { Integration, FromDate = FromDate?.ToString("yyyy-MM-dd"), ToDate = ToDate?.ToString("yyyy-MM-dd"), Status, JournalName });

            // Subquery for batch IDs to avoid SQL Server "WITH" / semicolon issue with Contains(list)
            var batchIdSubquery = _dbResolver.GetFeedBatches(ResolvedIntegration).AsQueryable();
            if (FromDate.HasValue)
                batchIdSubquery = batchIdSubquery.Where(b => (b.AETransactionDate ?? b.DateSent) >= FromDate.Value.Date);
            if (ToDate.HasValue)
                batchIdSubquery = batchIdSubquery.Where(b => (b.AETransactionDate ?? b.DateSent) < ToDate.Value.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(Status))
            {
                var s = Status.Trim().ToUpper();
                batchIdSubquery = batchIdSubquery.Where(b => b.AERequestStatus != null && b.AERequestStatus.ToUpper() == s);
            }
            if (!string.IsNullOrWhiteSpace(JournalName))
                batchIdSubquery = batchIdSubquery.Where(b => b.AEJournalName.Contains(JournalName.Trim()));
            var batchIdsQuery = batchIdSubquery
                .OrderByDescending(b => b.AETransactionDate ?? b.DateSent)
                .ThenByDescending(b => b.BatchID)
                .Take(200)
                .Select(b => b.BatchID);

            var items = await feedItems
                .Where(i => batchIdsQuery.Contains(i.BatchID))
                .OrderBy(i => i.TransactionDate)
                .ThenBy(i => i.RecordID)
                .Take(5000)
                .ToListAsync();

            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Batches.xlsx
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Batches");
                    int row = 1;
                    ws.Cell(row, 1).Value = "BatchID"; ws.Cell(row, 2).Value = "DateSent"; ws.Cell(row, 3).Value = "PickupSentFlag";
                    ws.Cell(row, 4).Value = "AEConsumerReferenceID"; ws.Cell(row, 5).Value = "AEConsumerNotes"; ws.Cell(row, 6).Value = "AERequestStatus";
                    ws.Cell(row, 7).Value = "ErrorDetail"; ws.Cell(row, 8).Value = "AEConsumerRequestID"; ws.Cell(row, 9).Value = "AETransactionDate";
                    ws.Cell(row, 10).Value = "AEJournalName"; ws.Cell(row, 11).Value = "AEJournalDescription"; ws.Cell(row, 12).Value = "AEJournalReference"; ws.Cell(row, 13).Value = "BatchTotal";
                    row++;
                    foreach (var b in batches)
                    {
                        ws.Cell(row, 1).Value = b.BatchID.ToString(); ws.Cell(row, 2).Value = b.DateSent; ws.Cell(row, 3).Value = b.PickupSentFlag;
                        ws.Cell(row, 4).Value = b.AEConsumerReferenceID; ws.Cell(row, 5).Value = b.AEConsumnerNotes; ws.Cell(row, 6).Value = b.AERequestStatus;
                        ws.Cell(row, 7).Value = b.ErrorDetail; ws.Cell(row, 8).Value = b.AEConsumerRequestID?.ToString() ?? "";
                        ws.Cell(row, 9).Value = b.AETransactionDate; ws.Cell(row, 10).Value = b.AEJournalName;
                        ws.Cell(row, 11).Value = b.AEJournalDescription; ws.Cell(row, 12).Value = b.AEJournalReference; ws.Cell(row, 13).Value = b.BatchTotal;
                        row++;
                    }
                    ws.Columns().AdjustToContents();
                    var batchEntry = zip.CreateEntry("Batches.xlsx");
                    using (var entryStream = batchEntry.Open())
                    using (var ms = new MemoryStream())
                    {
                        wb.SaveAs(ms);
                        ms.Position = 0;
                        ms.CopyTo(entryStream);
                    }
                }

                // Items.xlsx (include BatchID so items can be tied to Batches sheet)
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Items");
                    int row = 1;
                    ws.Cell(row, 1).Value = "BatchID"; ws.Cell(row, 2).Value = "RecordID"; ws.Cell(row, 3).Value = "OriginalDocNumber"; ws.Cell(row, 4).Value = "DebitChartString"; ws.Cell(row, 5).Value = "CreditChartString";
                    ws.Cell(row, 6).Value = "TransactionDate"; ws.Cell(row, 7).Value = "Quantity"; ws.Cell(row, 8).Value = "UnitPrice"; ws.Cell(row, 9).Value = "TotalCharge";
                    ws.Cell(row, 10).Value = "ClientID"; ws.Cell(row, 11).Value = "SystemID"; ws.Cell(row, 12).Value = "DebitStringValid"; ws.Cell(row, 13).Value = "CreditStringValid";
                    ws.Cell(row, 14).Value = "TestCode"; ws.Cell(row, 15).Value = "TestName"; ws.Cell(row, 16).Value = "UnprocessedCOAString"; ws.Cell(row, 17).Value = "AE_Details";
                    row++;
                    foreach (var i in items)
                    {
                        var aeDetails = $"D: {(string.IsNullOrWhiteSpace(i.DebitValidationError) ? "-" : i.DebitValidationError)} | C: {(string.IsNullOrWhiteSpace(i.CreditValidationError) ? "-" : i.CreditValidationError)}";
                        ws.Cell(row, 1).Value = i.BatchID.ToString(); ws.Cell(row, 2).Value = i.RecordID.ToString(); ws.Cell(row, 3).Value = i.OrignalDocNumber; ws.Cell(row, 4).Value = i.DebitChartString; ws.Cell(row, 5).Value = i.CreditChartString;
                        ws.Cell(row, 6).Value = i.TransactionDate; ws.Cell(row, 7).Value = i.Quantity; ws.Cell(row, 8).Value = i.UnitPrice; ws.Cell(row, 9).Value = i.TotalCharge;
                        ws.Cell(row, 10).Value = i.ClientID; ws.Cell(row, 11).Value = i.SystemID; ws.Cell(row, 12).Value = i.DebitStringValid; ws.Cell(row, 13).Value = i.CreditStringValid;
                        ws.Cell(row, 14).Value = i.TestCode; ws.Cell(row, 15).Value = i.TestName; ws.Cell(row, 16).Value = i.UnprocessedCOAString ?? ""; ws.Cell(row, 17).Value = aeDetails;
                        row++;
                    }
                    ws.Columns().AdjustToContents();
                    var itemEntry = zip.CreateEntry("Items.xlsx");
                    using (var entryStream = itemEntry.Open())
                    using (var ms = new MemoryStream())
                    {
                        wb.SaveAs(ms);
                        ms.Position = 0;
                        ms.CopyTo(entryStream);
                    }
                }
            }
            // ZipArchive writes central directory on Dispose; leaveOpen: true keeps stream open
            zipStream.Position = 0;
            var zipFileName = $"AE_Feed_All_{ResolvedIntegration}_{ts}.zip";
            return File(zipStream.ToArray(), "application/zip", zipFileName);
        }

        // ============================================================
        // GET: Load batches + items + AE details (if requested)
        // ============================================================
        public async Task OnGetAsync()
        {
            // clear AE detail state every load
            AeDetailData = null;
            AeDetailError = null;
            AeDetailInputCoa = null;

            var feedBatches = _dbResolver.GetFeedBatches(ResolvedIntegration);
            var feedItems = _dbResolver.GetFeedItems(ResolvedIntegration);

            // ---- Load Batches
            var batchQuery = feedBatches.AsQueryable();

            if (FromDate.HasValue)
            {
                var from = FromDate.Value.Date;
                batchQuery = batchQuery.Where(b => (b.AETransactionDate ?? b.DateSent) >= from);
            }

            if (ToDate.HasValue)
            {
                var to = ToDate.Value.Date.AddDays(1);
                batchQuery = batchQuery.Where(b => (b.AETransactionDate ?? b.DateSent) < to);
            }

            if (!string.IsNullOrWhiteSpace(Status))
            {
                var s = Status.Trim().ToUpper();
                batchQuery = batchQuery.Where(b => b.AERequestStatus != null && b.AERequestStatus.ToUpper() == s);
            }

            if (!string.IsNullOrWhiteSpace(JournalName))
            {
                var j = JournalName.Trim();
                batchQuery = batchQuery.Where(b => b.AEJournalName.Contains(j));
            }

            Batches = await batchQuery
                .OrderByDescending(b => b.AETransactionDate ?? b.DateSent)
                .ThenByDescending(b => b.BatchID)
                .Take(200)
                .ToListAsync();

            var batchIds = Batches.Select(b => b.BatchID).ToList();
            if (batchIds.Count > 0)
            {
                var batchIdsQuery = feedBatches.AsQueryable();
                if (FromDate.HasValue) batchIdsQuery = batchIdsQuery.Where(b => (b.AETransactionDate ?? b.DateSent) >= FromDate.Value.Date);
                if (ToDate.HasValue) batchIdsQuery = batchIdsQuery.Where(b => (b.AETransactionDate ?? b.DateSent) < ToDate.Value.Date.AddDays(1));
                if (!string.IsNullOrWhiteSpace(Status)) { var s = Status.Trim().ToUpper(); batchIdsQuery = batchIdsQuery.Where(b => b.AERequestStatus != null && b.AERequestStatus.ToUpper() == s); }
                if (!string.IsNullOrWhiteSpace(JournalName)) batchIdsQuery = batchIdsQuery.Where(b => b.AEJournalName.Contains(JournalName.Trim()));
                var bIds = batchIdsQuery.OrderByDescending(b => b.AETransactionDate ?? b.DateSent).ThenByDescending(b => b.BatchID).Take(200).Select(b => b.BatchID);
                InvalidItemsCount = await feedItems
                    .Where(i => bIds.Contains(i.BatchID) && (i.DebitStringValid == "Invalid" || i.CreditStringValid == "Invalid"))
                    .CountAsync();
            }
            else
                InvalidItemsCount = 0;

            // ---- Default Selected Batch
            if (!SelectedBatchId.HasValue && Batches.Any())
                SelectedBatchId = Batches.First().BatchID;

            // ---- Load Items for selected batch
            if (SelectedBatchId.HasValue)
            {
                var selectedBatch = Batches.FirstOrDefault(b => b.BatchID == SelectedBatchId.Value);
                SelectedBatchJournalName = selectedBatch?.AEJournalName;

                var itemsQuery = feedItems
                    .Where(i => i.BatchID == SelectedBatchId.Value);

                if (ShowInvalidOnly)
                {
                    itemsQuery = itemsQuery.Where(i =>
                        i.DebitStringValid == "Invalid" || i.CreditStringValid == "Invalid");
                }

                Items = await itemsQuery
                    .OrderBy(i => i.TransactionDate)
                    .ThenBy(i => i.RecordID)
                    .ToListAsync();
            }

            // ---- Load AE details when user clicks "Debit/Credit"
            if (SelectedBatchId.HasValue && DetailRecordId.HasValue && !string.IsNullOrWhiteSpace(DetailSide))
            {
                var item = Items.FirstOrDefault(x => x.RecordID == DetailRecordId.Value);
                if (item == null)
                {
                    AeDetailError = "Item not found in current batch items.";
                    return;
                }

                AeDetailInputCoa = DetailSide == "D" ? item.DebitChartString : item.CreditChartString;

                if (string.IsNullOrWhiteSpace(AeDetailInputCoa))
                {
                    AeDetailError = "Selected COA is empty.";
                    return;
                }

                var op = await _ae.GlValidateChartstring.ExecuteAsync(AeDetailInputCoa, true);

                if (op.Errors?.Any() == true)
                    AeDetailError = string.Join(" | ", op.Errors.Select(e => e.Message));

                AeDetailData = op.Data;

                if (AeDetailData == null && string.IsNullOrWhiteSpace(AeDetailError))
                    AeDetailError = "AE returned no data.";
            }
        }
    }
}
