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
        private readonly IHiwuFileRepository _files;

        public FilesModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            HiwuReadService readService,
            HiwuCoflFileOrderService orders,
            IHiwuFileRepository files)
            : base(integrationService, authorizationService)
        {
            _readService = readService;
            _orders = orders;
            _files = files;
        }

        public IReadOnlyList<HiwuImportFile> Files { get; private set; } = Array.Empty<HiwuImportFile>();
        public IReadOnlyDictionary<Guid, string> TraceFirstStatus { get; private set; } = new Dictionary<Guid, string>();
        public IReadOnlyDictionary<Guid, HiwuReviewProgress> ReviewProgress { get; private set; } = new Dictionary<Guid, HiwuReviewProgress>();

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        [BindProperty(Name = "q", SupportsGet = true)]
        public string? Q { get; set; }

        public int TotalCount { get; private set; }
        public int ReceivedCount { get; private set; }
        public int AmendedCount { get; private set; }
        public int ImportedCount { get; private set; }
        public int ParseErrorCount { get; private set; }

        public bool HasSearch => !string.IsNullOrWhiteSpace(Q);

        public async Task<IActionResult> OnGetAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            await _files.BackfillSampleIndexAsync();
            (Files, TraceFirstStatus) = await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnGetExportAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            await _files.BackfillSampleIndexAsync();
            var (files, traceFirst) = await LoadAsync();
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Manifest files");
            sheet.Cell(1, 1).Value = "File name";
            sheet.Cell(1, 2).Value = "Samples";
            sheet.Cell(1, 3).Value = "Has B-Sample";
            sheet.Cell(1, 4).Value = "Date downloaded";
            sheet.Cell(1, 5).Value = "Status";
            var amendedExport = string.Equals(Status, HiwuParseStatus.Amended, StringComparison.OrdinalIgnoreCase);
            sheet.Cell(1, 6).Value = "TraceFirst";
            if (amendedExport)
                sheet.Cell(1, 7).Value = "Reviewed";
            var header = sheet.Range(1, 1, 1, amendedExport ? 7 : 6);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightGray;

            var row = 2;
            foreach (var file in files)
            {
                sheet.Cell(row, 1).Value = file.RemoteFileName;
                sheet.Cell(row, 2).Value = file.SampleCount;
                sheet.Cell(row, 3).Value = file.HasBSample == true ? "Yes" : "";
                sheet.Cell(row, 4).Value = ToPacific(file.ReceivedUtc).ToString("yyyy-MM-dd HH:mm");
                sheet.Cell(row, 5).Value = HiwuParseStatus.Label(file.ParseStatus);
                sheet.Cell(row, 6).Value = traceFirst.TryGetValue(file.FileId, out var traceStatus) ? traceStatus : "Not started";
                if (amendedExport)
                    sheet.Cell(row, 7).Value = ReviewProgress.TryGetValue(file.FileId, out var reviewed) ? reviewed.Label : "";
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
            var all = await _readService.ListFilesAsync(FromDate, ToDate, null, Q);
            var statuses = await _orders.ListFileStatusesAsync(all);
            TotalCount = all.Count;
            ReceivedCount = all.Count(file => file.ParseStatus == HiwuParseStatus.Received);
            AmendedCount = all.Count(file => file.ParseStatus == HiwuParseStatus.Amended);
            ParseErrorCount = all.Count(file => file.ParseStatus == HiwuParseStatus.ParseError);
            ImportedCount = all.Count(file =>
                statuses.TryGetValue(file.FileId, out var traceFirst)
                && traceFirst == HiwuCoflSampleStatus.Imported);

            IReadOnlyList<HiwuImportFile> shown;
            if (string.IsNullOrWhiteSpace(Status))
                shown = all;
            else if (string.Equals(Status, HiwuCoflSampleStatus.Imported, StringComparison.OrdinalIgnoreCase))
            {
                shown = all
                    .Where(file => statuses.TryGetValue(file.FileId, out var traceFirst) && traceFirst == HiwuCoflSampleStatus.Imported)
                    .ToList();
            }
            else
            {
                shown = all
                    .Where(file => string.Equals(file.ParseStatus, Status, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            ReviewProgress = string.Equals(Status, HiwuParseStatus.Amended, StringComparison.OrdinalIgnoreCase)
                ? await ReviewLabelsAsync(shown)
                : new Dictionary<Guid, HiwuReviewProgress>();
            return (shown, statuses);
        }

        private async Task<IReadOnlyDictionary<Guid, HiwuReviewProgress>> ReviewLabelsAsync(IReadOnlyList<HiwuImportFile> files)
        {
            var amended = files.Where(file => file.ParseStatus == HiwuParseStatus.Amended).ToList();
            if (amended.Count == 0)
                return new Dictionary<Guid, HiwuReviewProgress>();

            var counts = await _readService.CountSampleReviewsAsync(amended.Select(file => file.FileId).ToList());
            var labels = new Dictionary<Guid, HiwuReviewProgress>();
            foreach (var file in amended)
            {
                var total = file.SampleCount;
                var reviewed = counts.TryGetValue(file.FileId, out var count) ? count : 0;
                if (reviewed > total)
                    reviewed = total;
                labels[file.FileId] = new HiwuReviewProgress(reviewed, total);
            }

            return labels;
        }

        public static DateTime ToPacific(DateTime utc)
        {
            var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            return TimeZoneInfo.ConvertTimeFromUtc(value, zone);
        }

        public sealed record HiwuReviewProgress(int Reviewed, int Total)
        {
            public string Label => Reviewed + " of " + Total + " reviewed";
            public bool Complete => Total > 0 && Reviewed == Total;
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
