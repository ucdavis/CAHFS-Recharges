using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Hiwu;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CAHFS_Recharges.Pages.Integrations.Hiwu
{
    [Authorize(Policy = CaeiPolicies.ViewerPolicy)]
    public class DetailModel : IntegrationPageModel
    {
        private readonly HiwuReadService _readService;

        public DetailModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService,
            HiwuReadService readService)
            : base(integrationService, authorizationService)
        {
            _readService = readService;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public HiwuImportFile? Manifest { get; private set; }
        public HiwuManifestDocument? Document { get; private set; }
        public HiwuManifestDocument? OriginalDocument { get; private set; }
        public IReadOnlyList<HiwuFieldCompare> MissionCompare { get; private set; } = Array.Empty<HiwuFieldCompare>();
        public IReadOnlyList<HiwuSampleCompare> SampleCompare { get; private set; } = Array.Empty<HiwuSampleCompare>();
        public IReadOnlyList<string> SampleColumns { get; private set; } = Array.Empty<string>();
        public bool OriginalMissing { get; private set; }
        public bool CanDownloadCsv =>
            Manifest != null && Manifest.ParseStatus is HiwuParseStatus.Received or HiwuParseStatus.Amended;

        public async Task<IActionResult> OnGetAsync()
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            Manifest = await _readService.GetFileAsync(Id);
            if (Manifest == null)
                return Page();

            if (Manifest.ParseStatus == HiwuParseStatus.ParseError)
                return Page();

            if (!HiwuManifestDocument.TryRead(Manifest.RawXml, out var document))
                return Page();

            Document = document;
            SampleColumns = Columns(document);

            if (Manifest.IsAmendment)
            {
                if (Manifest.OriginalFile == null || !HiwuManifestDocument.TryRead(Manifest.OriginalFile.RawXml, out var original))
                {
                    OriginalMissing = true;
                }
                else
                {
                    OriginalDocument = original;
                    MissionCompare = document.CompareMission(original);
                    SampleCompare = document.CompareSamples(original);
                }
            }

            return Page();
        }

        public async Task<IActionResult> OnGetDownloadXmlAsync(Guid id)
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            var file = await _readService.GetFileAsync(id);
            if (file == null)
                return NotFound();

            var bytes = System.Text.Encoding.UTF8.GetBytes(file.RawXml ?? "");
            return File(bytes, "application/xml", Path.GetFileName(file.RemoteFileName));
        }

        public async Task<IActionResult> OnGetDownloadCsvAsync(Guid id)
        {
            var redirect = RedirectCahfs();
            if (redirect != null)
                return redirect;

            var file = await _readService.GetFileAsync(id);
            if (file == null)
                return NotFound();
            if (file.ParseStatus is not (HiwuParseStatus.Received or HiwuParseStatus.Amended))
                return NotFound();
            if (!HiwuManifestDocument.TryRead(file.RawXml, out var document))
                return NotFound();

            var csv = HiwuCsvWriter.Write(document);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", HiwuCsvWriter.DownloadName(file.RemoteFileName));
        }

        public static DateTime ToPacific(DateTime utc)
        {
            return FilesModel.ToPacific(utc);
        }

        private static IReadOnlyList<string> Columns(HiwuManifestDocument document)
        {
            var columns = HiwuManifestDocument.SampleFields
                .Where(field => !HiwuManifestDocument.OptionalSampleFields.Contains(field, StringComparer.Ordinal))
                .ToList();
            foreach (var field in HiwuManifestDocument.OptionalSampleFields)
            {
                if (document.Samples.Any(sample => sample.Fields.ContainsKey(field) && sample.Fields[field].Length > 0))
                    columns.Add(field);
            }

            return columns;
        }

        private IActionResult? RedirectCahfs()
        {
            if (CurrentIntegration == IntegrationType.EQUINE)
                return null;

            var pathBase = HttpContext.Request.PathBase.Value ?? "";
            return Redirect(pathBase + "/Integrations/EQUINE/Hiwu/Files/Detail" + Request.QueryString.Value);
        }
    }
}
