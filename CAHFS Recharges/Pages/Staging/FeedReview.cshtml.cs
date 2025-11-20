using ClosedXML.Excel;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;



namespace CAHFS_Recharges.Pages.Staging
{
    public class FeedReviewModel : PageModel
    {
        private readonly FinancialContext _context;

        public FeedReviewModel(FinancialContext context)
        {
            _context = context;
        }

        // Common filters
        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? JournalName { get; set; }

        // Which batch's items to show
        [BindProperty(SupportsGet = true)]
        public Guid? SelectedBatchId { get; set; }

        public IList<FeedBatch> Batches { get; set; } = new List<FeedBatch>();
        public IList<FeedItem> Items { get; set; } = new List<FeedItem>();

        // Download Item's data in Excel format
        public async Task<IActionResult> OnGetDownloadItemsAsync()
        {
            if (!SelectedBatchId.HasValue)
            {
                return RedirectToPage();
            }

            var items = await _context.FeedItems
                .Where(i => i.BatchID == SelectedBatchId.Value)
                .OrderBy(i => i.TransactionDate)
                .ThenBy(i => i.RecordID)
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Items");

            int row = 1;

            ws.Cell(row, 1).Value = "recordID";
            ws.Cell(row, 2).Value = "originalDocNumber";
            ws.Cell(row, 3).Value = "receivablesChartString";
            ws.Cell(row, 4).Value = "incomeChartString";
            ws.Cell(row, 5).Value = "transactionDate";
            ws.Cell(row, 6).Value = "quantity";
            ws.Cell(row, 7).Value = "unitPrice";
            ws.Cell(row, 8).Value = "totalCharge";
            ws.Cell(row, 9).Value = "clientID";
            ws.Cell(row, 10).Value = "systemID";
            ws.Cell(row, 11).Value = "incomeStringValid";
            ws.Cell(row, 12).Value = "receivableStringValid";
            ws.Cell(row, 13).Value = "description";

            row++;

            foreach (var i in items)
            {
                ws.Cell(row, 1).Value = i.RecordID.ToString();
                ws.Cell(row, 2).Value = i.OrignalDocNumber;
                ws.Cell(row, 3).Value = i.ReceivablesChartString;
                ws.Cell(row, 4).Value = i.IncomeChartString;
                ws.Cell(row, 5).Value = i.TransactionDate;
                ws.Cell(row, 6).Value = i.Quantity;
                ws.Cell(row, 7).Value = i.UnitPrice;
                ws.Cell(row, 8).Value = i.TotalCharge;
                ws.Cell(row, 9).Value = i.ClientID;
                ws.Cell(row, 10).Value = i.SystemID;
                ws.Cell(row, 11).Value = i.IncomeStringValid;
                ws.Cell(row, 12).Value = i.ReceivableStringValid;
                ws.Cell(row, 13).Value = i.Description;

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"AE_Feed_Items_{SelectedBatchId.Value}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }


        // Download Batch data in Excel format
        public async Task<IActionResult> OnGetDownloadBatchesAsync()
        {
            // Build the same filtered query
            var query = _context.FeedBatches.AsQueryable();

            if (FromDate.HasValue)
            {
                var from = FromDate.Value.Date;
                query = query.Where(b =>
                    (b.AETransactionDate ?? b.DateSent) >= from);
            }

            if (ToDate.HasValue)
            {
                var to = ToDate.Value.Date.AddDays(1);
                query = query.Where(b =>
                    (b.AETransactionDate ?? b.DateSent) < to);
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
                query = query.Where(b => b.AEJournalName != null &&
                                         b.AEJournalName.Contains(j));
            }

            var batches = await query
                .OrderByDescending(b => b.AETransactionDate ?? b.DateSent)
                .ThenByDescending(b => b.BatchID)
                .Take(200)
                .ToListAsync();

            // Build Excel workbook
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Batches");

            int row = 1;

            // header row
            ws.Cell(row, 1).Value = "batchID";
            ws.Cell(row, 2).Value = "dateSent";
            ws.Cell(row, 3).Value = "pickupSentFlag";
            ws.Cell(row, 4).Value = "AEConsumerReferenceID";
            ws.Cell(row, 5).Value = "AEConsumerNotes";
            ws.Cell(row, 6).Value = "AERequestStatus";
            ws.Cell(row, 7).Value = "ErrorDetail";
            ws.Cell(row, 8).Value = "AEConsumerRequestID";
            ws.Cell(row, 9).Value = "AETransactionDate";
            ws.Cell(row, 10).Value = "AEJournalName";
            ws.Cell(row, 11).Value = "AEJournalDescription";
            ws.Cell(row, 12).Value = "AEJournalReference";
            ws.Cell(row, 13).Value = "batchTotal";

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
                ws.Cell(row, 8).Value = b.AEConsumerRequestID?.ToString() ?? string.Empty;
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

            var fileName = $"AE_Feed_Batch_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }


        public async Task OnGetAsync()
        {
            // ----- 1) Get batches with filters -----
            var batchQuery = _context.FeedBatches.AsQueryable();

            if (FromDate.HasValue)
            {
                var from = FromDate.Value.Date;
                batchQuery = batchQuery.Where(b =>
                    (b.AETransactionDate ?? b.DateSent) >= from);
            }

            if (ToDate.HasValue)
            {
                var to = ToDate.Value.Date.AddDays(1);
                batchQuery = batchQuery.Where(b =>
                    (b.AETransactionDate ?? b.DateSent) < to);
            }

            if (!string.IsNullOrWhiteSpace(Status))
            {
                var s = Status.Trim().ToUpper();
                batchQuery = batchQuery.Where(b => b.AERequestStatus != null &&
                                                   b.AERequestStatus.ToUpper() == s);
            }

            if (!string.IsNullOrWhiteSpace(JournalName))
            {
                var j = JournalName.Trim();
                batchQuery = batchQuery.Where(b => b.AEJournalName != null &&
                                                   b.AEJournalName.Contains(j));
            }

            batchQuery = batchQuery
                .OrderByDescending(b => b.AETransactionDate ?? b.DateSent)
                .ThenByDescending(b => b.BatchID);

            Batches = await batchQuery.Take(200).ToListAsync();

            // Default selected batch = first row if none specified
            if (!SelectedBatchId.HasValue && Batches.Any())
            {
                SelectedBatchId = Batches.First().BatchID;
            }

            // ----- 2) Get items for selected batch -----
            if (SelectedBatchId.HasValue)
            {
                Items = await _context.FeedItems
                    .Where(i => i.BatchID == SelectedBatchId.Value)
                    .OrderBy(i => i.TransactionDate)
                    .ThenBy(i => i.RecordID)
                    .ToListAsync();
            }
        }
    }
}
