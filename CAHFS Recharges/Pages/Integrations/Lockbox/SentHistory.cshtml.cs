using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using CAHFS_Recharges.Services;
using CAHFS_Recharges.Services.Lockbox;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CAHFS_Recharges.Pages.Integrations.Lockbox
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class SentHistoryModel : IntegrationPageModel
    {
        private readonly LockboxReadService _readService;

        public SentHistoryModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            LockboxReadService readService)
            : base(integrationService, authorizationService)
        {
            _readService = readService;
        }

        private IntegrationType ResolvedIntegration => CurrentIntegration ?? IntegrationType.CAHFS;

        public LockboxPostHistorySummary Summary { get; private set; } = new();
        public IReadOnlyList<LockboxPostHistoryRow> Rows { get; private set; } = Array.Empty<LockboxPostHistoryRow>();

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        /// <summary>Empty / All = Posted + Failed. Otherwise Posted or Failed.</summary>
        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? BillingId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DocumentNumber { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? FileName { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (CurrentIntegration == null)
                return RedirectToPage("/Home");

            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            if (CurrentIntegration == null)
                return RedirectToPage("/Home");

            var rows = await _readService.GetPostHistoryRowsAsync(
                ResolvedIntegration,
                FromDate?.Date,
                ToDate?.Date,
                Status,
                BillingId,
                DocumentNumber,
                FileName);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Sent History");

            ws.Cell(1, 1).Value = "StagingId";
            ws.Cell(1, 2).Value = "PostedAt";
            ws.Cell(1, 3).Value = "PostStatus";
            ws.Cell(1, 4).Value = "BillingId";
            ws.Cell(1, 5).Value = "CheckNumber";
            ws.Cell(1, 6).Value = "CheckAmount";
            ws.Cell(1, 7).Value = "DepositDate";
            ws.Cell(1, 8).Value = "BatchNumber";
            ws.Cell(1, 9).Value = "FileName";
            ws.Cell(1, 10).Value = "ExternalDocNumber";
            ws.Cell(1, 11).Value = "PostError";

            var header = ws.Range(1, 1, 1, 11);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightGray;

            var rowNum = 2;
            foreach (var r in rows)
            {
                ws.Cell(rowNum, 1).Value = r.StagingId;
                ws.Cell(rowNum, 2).Value = r.PostedAt;
                ws.Cell(rowNum, 3).Value = r.PostStatus;
                ws.Cell(rowNum, 4).Value = r.BillingId ?? "";
                ws.Cell(rowNum, 5).Value = r.CheckNumber;
                ws.Cell(rowNum, 6).Value = r.CheckAmount;
                ws.Cell(rowNum, 7).Value = r.DepositDate;
                ws.Cell(rowNum, 8).Value = r.BatchNumber;
                ws.Cell(rowNum, 9).Value = r.FileName;
                ws.Cell(rowNum, 10).Value = r.ExternalDocNumber ?? "";
                ws.Cell(rowNum, 11).Value = r.PostError ?? "";
                rowNum++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            var integration = ResolvedIntegration.ToString();
            var fileName = $"Lockbox_SentHistory_{integration}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        private async Task LoadAsync()
        {
            Summary = await _readService.GetPostHistorySummaryAsync(
                ResolvedIntegration,
                FromDate?.Date,
                ToDate?.Date,
                Status,
                BillingId,
                DocumentNumber,
                FileName);

            Rows = await _readService.GetPostHistoryRowsAsync(
                ResolvedIntegration,
                FromDate?.Date,
                ToDate?.Date,
                Status,
                BillingId,
                DocumentNumber,
                FileName);
        }
    }
}
