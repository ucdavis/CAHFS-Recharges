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

        public FilesModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            HiwuReadService readService)
            : base(integrationService, authorizationService)
        {
            _readService = readService;
        }

        public IReadOnlyList<HiwuImportFile> Files { get; private set; } = Array.Empty<HiwuImportFile>();

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        public int ReceivedCount => Files.Count(f => f.ParseStatus == HiwuParseStatus.Received);
        public int AmendedCount => Files.Count(f => f.ParseStatus == HiwuParseStatus.Amended);
        public int ParseErrorCount => Files.Count(f => f.ParseStatus == HiwuParseStatus.ParseError);

        public async Task<IActionResult> OnGetAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            Files = await _readService.ListFilesAsync(FromDate, ToDate, Status);
            return Page();
        }

        public async Task<IActionResult> OnGetExportAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            var files = await _readService.ListFilesAsync(FromDate, ToDate, Status);
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Manifest files");
            sheet.Cell(1, 1).Value = "File name";
            sheet.Cell(1, 2).Value = "Date downloaded";
            sheet.Cell(1, 3).Value = "Status";
            var header = sheet.Range(1, 1, 1, 3);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightGray;

            var row = 2;
            foreach (var file in files)
            {
                sheet.Cell(row, 1).Value = file.RemoteFileName;
                sheet.Cell(row, 2).Value = ToPacific(file.ReceivedUtc).ToString("yyyy-MM-dd HH:mm");
                sheet.Cell(row, 3).Value = HiwuParseStatus.Label(file.ParseStatus);
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
