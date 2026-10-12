using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Hiwu;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CAHFS_Recharges.Pages.Integrations.Hiwu
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class FilesModel : IntegrationPageModel
    {
        private readonly HiwuReadService _readService;
        private readonly HiwuCoflFileOrderService _orders;

        public FilesModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            HiwuReadService readService,
            HiwuCoflFileOrderService orders)
            : base(integrationService, authorizationService)
        {
            _readService = readService;
            _orders = orders;
        }

        public IReadOnlyList<HiwuImportFile> Files { get; private set; } = Array.Empty<HiwuImportFile>();
        public IReadOnlyDictionary<Guid, string> TraceFirstStatus { get; private set; } = new Dictionary<Guid, string>();

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        public int TotalCount { get; private set; }
        public int ReceivedCount { get; private set; }
        public int AmendedCount { get; private set; }
        public int ImportedCount { get; private set; }
        public int ParseErrorCount { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            (Files, TraceFirstStatus) = await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnGetExportAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            var (files, traceFirst) = await LoadAsync();
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Manifest files");
            sheet.Cell(1, 1).Value = "File name";
            sheet.Cell(1, 2).Value = "Date downloaded";
            sheet.Cell(1, 3).Value = "Status";
            sheet.Cell(1, 4).Value = "TraceFirst";
            var header = sheet.Range(1, 1, 1, 4);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightGray;

            var row = 2;
            foreach (var file in files)
            {
                sheet.Cell(row, 1).Value = file.RemoteFileName;
                sheet.Cell(row, 2).Value = ToPacific(file.ReceivedUtc).ToString("yyyy-MM-dd HH:mm");
                sheet.Cell(row, 3).Value = HiwuParseStatus.Label(file.ParseStatus);
                sheet.Cell(row, 4).Value = traceFirst.TryGetValue(file.FileId, out var traceStatus) ? traceStatus : "Not started";
                row++;
            }

            sheet.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "HIWU-manifest-files.xlsx");
        }

        private async Task<(IReadOnlyList<HiwuImportFile> Files, IReadOnlyDictionary<Guid, string> Statuses)> LoadAsync()
        {
            var all = await _readService.ListFilesAsync(FromDate, ToDate, null);
            var statuses = await _orders.ListFileStatusesAsync(all);
            TotalCount = all.Count;
            ReceivedCount = all.Count(file => file.ParseStatus == HiwuParseStatus.Received);
            AmendedCount = all.Count(file => file.ParseStatus == HiwuParseStatus.Amended);
            ParseErrorCount = all.Count(file => file.ParseStatus == HiwuParseStatus.ParseError);
            ImportedCount = all.Count(file =>
                statuses.TryGetValue(file.FileId, out var traceFirst)
                && traceFirst == HiwuCoflSampleStatus.Imported);

            if (string.IsNullOrWhiteSpace(Status))
                return (all, statuses);
            if (string.Equals(Status, HiwuCoflSampleStatus.Imported, StringComparison.OrdinalIgnoreCase))
            {
                var imported = all
                    .Where(file => statuses.TryGetValue(file.FileId, out var traceFirst) && traceFirst == HiwuCoflSampleStatus.Imported)
                    .ToList();
                return (imported, statuses);
            }

            var matched = all
                .Where(file => string.Equals(file.ParseStatus, Status, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return (matched, statuses);
        }

        public static DateTime ToPacific(DateTime utc)
        {
            var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            return TimeZoneInfo.ConvertTimeFromUtc(value, zone);
        }

        private IActionResult? RedirectCahfs()
        {
            if (CurrentIntegration == IntegrationType.EQUINE)
                return null;

            var pathBase = HttpContext.Request.PathBase.Value ?? "";
            return Redirect(pathBase + "/Integrations/EQUINE/Hiwu/Files" + Request.QueryString.Value);
        }
    }
}
