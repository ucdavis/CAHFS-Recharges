using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuCoflFileOrderResult
    {
        public required string Message { get; init; }
        public string Status { get; init; } = HiwuCoflOrderResult.Failed;
        public IReadOnlyList<HiwuCoflSampleRow> Samples { get; init; } = [];
    }

    /// Builds one accession for a Received file and creates each sample on it.
    public sealed class HiwuCoflFileOrderService
    {
        private readonly CahfsIntegrationsContext _db;
        private readonly HiwuCoflCatalogCache _catalogs;
        private readonly HiwuCoflOrderService _orders;
        private readonly HiwuCoflClient _client;
        private readonly Dictionary<long, string?> _accessionNumbers = new();

        public HiwuCoflFileOrderService(
            CahfsIntegrationsContext db,
            HiwuCoflCatalogCache catalogs,
            HiwuCoflOrderService orders,
            HiwuCoflClient client)
        {
            _db = db;
            _catalogs = catalogs;
            _orders = orders;
            _client = client;
        }

        public async Task<IReadOnlyList<HiwuCoflSampleRow>> ListForFileAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            var rows = await _db.CoflSamples.AsNoTracking()
                .Where(row => row.FileId == fileId)
                .OrderBy(row => row.SampleKey)
                .ToListAsync(cancellationToken);
            await FillAccessionNumbersAsync(rows, cancellationToken);
            await ApplySentNamesAsync(rows, cancellationToken);
            return rows;
        }

        public async Task<IReadOnlyDictionary<Guid, string>> ListFileStatusesAsync(
            IReadOnlyList<HiwuImportFile> files,
            CancellationToken cancellationToken = default)
        {
            var ids = files.Select(file => file.FileId).ToList();
            var rows = await _db.CoflSamples.AsNoTracking()
                .Where(row => ids.Contains(row.FileId))
                .ToListAsync(cancellationToken);
            var byFile = rows.GroupBy(row => row.FileId).ToDictionary(group => group.Key, group => (IReadOnlyList<HiwuCoflSampleRow>)group.ToList());
            return files.ToDictionary(
                file => file.FileId,
                file => HiwuCoflSampleStatus.ForFile(
                    file.ParseStatus,
                    file.IsAmendment,
                    byFile.TryGetValue(file.FileId, out var saved) ? saved : []));
        }

        public async Task<IReadOnlyList<HiwuCoflWorkItem>> ListWorkAsync(
            string? fileName,
            bool notStarted,
            bool partial,
            bool failed,
            CancellationToken cancellationToken = default)
        {
            if (!notStarted && !partial && !failed)
                return [];

            var files = await _db.ImportFiles.AsNoTracking()
                .Where(file => file.ParseStatus == HiwuParseStatus.Received && !file.IsAmendment)
                .OrderByDescending(file => file.ReceivedUtc)
                .Take(1000)
                .ToListAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var needle = fileName.Trim();
                files = files
                    .Where(file => file.RemoteFileName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var statuses = await ListFileStatusesAsync(files, cancellationToken);
            var wanted = new HashSet<string>();
            if (notStarted)
                wanted.Add(HiwuCoflSampleStatus.NotStarted);
            if (partial)
                wanted.Add(HiwuCoflSampleStatus.Partial);
            if (failed)
                wanted.Add(HiwuCoflSampleStatus.Failed);

            return files
                .Where(file => statuses.TryGetValue(file.FileId, out var status) && wanted.Contains(status))
                .Take(200)
                .Select(file =>
                {
                    var status = statuses[file.FileId];
                    var count = HiwuManifestDocument.TryRead(file.RawXml, out var document) ? document.Samples.Count : 0;
                    return new HiwuCoflWorkItem
                    {
                        FileId = file.FileId,
                        FileName = file.RemoteFileName,
                        ReceivedUtc = file.ReceivedUtc,
                        SampleCount = count,
                        TraceFirstStatus = status,
                        NextStep = status == HiwuCoflSampleStatus.Partial
                            ? "Continue the remaining samples"
                            : "Create order"
                    };
                })
                .ToList();
        }

        public async Task<HiwuCoflWorkReview> ReviewAsync(HiwuImportFile file, CancellationToken cancellationToken = default)
        {
            var saved = await ListForFileAsync(file.FileId, cancellationToken);
            var status = HiwuCoflSampleStatus.ForFile(file.ParseStatus, file.IsAmendment, saved);
            if (file.IsAmendment || file.ParseStatus == HiwuParseStatus.Amended)
                return HiwuCoflWorkReview.Blocked(status, saved, "Amendments are not sent to TraceFirst.");
            if (file.ParseStatus != HiwuParseStatus.Received)
                return HiwuCoflWorkReview.Blocked(status, saved, "Only a received file can be created in TraceFirst.");
            if (!HiwuManifestDocument.TryRead(file.RawXml, out var document))
                return HiwuCoflWorkReview.Blocked(status, saved, "The stored file could not be read as a testMission document.");

            var catalog = await _catalogs.GetAsync(cancellationToken);
            return HiwuCoflWorkReview.Build(document, saved, catalog, status);
        }

        public async Task<IReadOnlyList<HiwuCoflSampleRow>> ListRecentAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _db.CoflSamples.AsNoTracking()
                .OrderByDescending(row => row.UpdatedUtc)
                .Take(200)
                .ToListAsync(cancellationToken);
            await FillAccessionNumbersAsync(rows, cancellationToken);
            await ApplySentNamesAsync(rows, cancellationToken);
            return rows;
        }

        public async Task<HiwuCoflFileOrderResult> CreateOrderAsync(HiwuImportFile file, CancellationToken cancellationToken = default)
        {
            if (file.IsAmendment || file.ParseStatus == HiwuParseStatus.Amended)
            {
                return new HiwuCoflFileOrderResult
                {
                    Message = "Amendments are not sent to TraceFirst.",
                    Samples = await ListForFileAsync(file.FileId, cancellationToken)
                };
            }

            if (file.ParseStatus != HiwuParseStatus.Received)
            {
                return new HiwuCoflFileOrderResult
                {
                    Message = "Only a received file can be created in TraceFirst.",
                    Samples = await ListForFileAsync(file.FileId, cancellationToken)
                };
            }

            if (!HiwuManifestDocument.TryRead(file.RawXml, out var document))
            {
                return new HiwuCoflFileOrderResult { Message = "The stored file could not be read as a testMission document." };
            }

            var saved = await _db.CoflSamples
                .Where(row => row.FileId == file.FileId)
                .ToListAsync(cancellationToken);
            var pending = document.Samples
                .Select((sample, index) => (sample, Key: SampleKey(sample, index)))
                .Where(item => saved.All(row => row.SampleKey != item.Key || row.Status != HiwuCoflSampleStatus.Imported))
                .ToList();
            if (pending.Count == 0)
            {
                return new HiwuCoflFileOrderResult
                {
                    Status = HiwuCoflOrderResult.Completed,
                    Message = "",
                    Samples = await ListForFileAsync(file.FileId, cancellationToken)
                };
            }

            var catalog = await _catalogs.GetAsync(cancellationToken);
            var problems = new List<string>();
            AddRequired(catalog, HiwuCoflCatalog.Labs, catalog.LabId, "Lab Equine Analytical Chemistry was not found.", problems);
            AddRequired(catalog, HiwuCoflCatalog.Clients, catalog.ClientId, "Client Horseracing Integrity and Welfare Unit (HIWU) was not found.", problems);
            AddRequired(catalog, HiwuCoflCatalog.Breeds, catalog.BreedId, "Breed Equidae :: Horse :: NFS was not found.", problems);
            AddRequired(catalog, HiwuCoflCatalog.AgeUnits, catalog.AgeUomId, "Age unit Years was not found.", problems);
            foreach (var name in new[]
            {
                HiwuCoflCatalog.CarriersList,
                HiwuCoflCatalog.SexCodesList,
                HiwuCoflCatalog.SpecimenTypesList,
                HiwuCoflCatalog.SpecimenDescriptionsList,
                HiwuCoflCatalog.TestSchedulesList
            })
            {
                if (catalog.LookupErrors.TryGetValue(name, out var error))
                    problems.Add(error);
            }

            var samples = new List<HiwuCoflOrderSample>();
            foreach (var item in pending)
            {
                var existing = saved.FirstOrDefault(row => row.SampleKey == item.Key);
                var built = HiwuCoflSampleMapper.Map(item.sample, item.Key, catalog, existing);
                if (built.Error != null)
                    problems.Add(built.Error);
                else if (built.Sample != null)
                    samples.Add(built.Sample);
            }

            if (problems.Count > 0)
            {
                return new HiwuCoflFileOrderResult { Message = string.Join(" ", problems) };
            }

            var accessionId = saved.Select(row => row.AccessionId).FirstOrDefault(id => id != null);
            var order = new HiwuCoflOrder
            {
                Operation = HiwuCoflOperation.CreateOrder,
                AccessionId = accessionId,
                Accession = accessionId == null
                    ? new HiwuCoflAccessionRequest
                    {
                        LabId = catalog.LabId!.Value,
                        ClientId = catalog.ClientId!.Value,
                        CarrierId = catalog.MatchCarrier(Field(document.Mission, "courier")),
                        CarrierRefNum = EmptyToNull(Field(document.Mission, "trackingRef")),
                        AdditionalInfo = EmptyToNull(AdditionalInfo(document))
                    }
                    : null,
                Samples = samples
            };

            var result = await _orders.ExecuteAsync(order, cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var sample in samples)
            {
                var row = saved.FirstOrDefault(item => item.SampleKey == sample.SampleKey);
                if (row == null)
                {
                    row = new HiwuCoflSampleRow { FileId = file.FileId, SampleKey = sample.SampleKey! };
                    _db.CoflSamples.Add(row);
                }

                var label = " " + sample.SampleKey;
                var animalStep = result.Steps.FirstOrDefault(step => step.Step == "Animal" + label);
                var specimenStep = result.Steps.FirstOrDefault(step => step.Step == "Specimen" + label);
                var testStep = result.Steps.FirstOrDefault(step => step.Step == "Tests" + label);
                row.AccessionId = result.AccessionId ?? row.AccessionId;
                row.AnimalId = animalStep?.ResourceId ?? sample.AnimalId ?? row.AnimalId;
                row.SpecimenId = specimenStep?.ResourceId ?? sample.SpecimenId ?? row.SpecimenId;
                row.UpdatedUtc = now;
                if (result.AccessionId == null)
                {
                    row.Status = HiwuCoflSampleStatus.Failed;
                    row.ErrorMessage = result.ErrorMessage;
                    continue;
                }

                row.ErrorMessage = animalStep is { Succeeded: false }
                    ? animalStep.ErrorMessage
                    : specimenStep is { Succeeded: false }
                        ? specimenStep.ErrorMessage
                        : testStep is { Succeeded: false } ? testStep.ErrorMessage : null;
                var testsRequired = sample.TestScheduleIds.Count > 0;
                row.Status = row.SpecimenId != null && (!testsRequired || testStep is { Succeeded: true })
                    ? HiwuCoflSampleStatus.Imported
                    : row.AnimalId != null || row.SpecimenId != null
                        ? HiwuCoflSampleStatus.Partial
                        : HiwuCoflSampleStatus.Failed;
                if (row.Status == HiwuCoflSampleStatus.Imported && !testsRequired)
                    row.ErrorMessage = "Tests were not assigned for this collection point.";
            }

            await _db.SaveChangesAsync(cancellationToken);
            var savedRows = await ListForFileAsync(file.FileId, cancellationToken);
            var accessionNumber = savedRows.Select(row => row.AccessionNumber).FirstOrDefault(number => !string.IsNullOrWhiteSpace(number))
                ?? result.AccessionId?.ToString();
            return new HiwuCoflFileOrderResult
            {
                Status = result.Status,
                Message = result.Status == HiwuCoflOrderResult.Completed
                    ? "The file was created in TraceFirst. Accession " + accessionNumber + "."
                    : result.ErrorMessage ?? "TraceFirst did not finish this file.",
                Samples = savedRows
            };
        }

        private async Task FillAccessionNumbersAsync(IReadOnlyList<HiwuCoflSampleRow> rows, CancellationToken cancellationToken)
        {
            foreach (var id in rows.Select(row => row.AccessionId).Where(id => id != null).Select(id => id!.Value).Distinct())
            {
                if (_accessionNumbers.ContainsKey(id))
                    continue;
                _accessionNumbers[id] = await _client.GetAccessionNumberAsync(id, cancellationToken);
            }

            foreach (var row in rows)
            {
                if (row.AccessionId != null && _accessionNumbers.TryGetValue(row.AccessionId.Value, out var number))
                    row.AccessionNumber = number;
            }
        }

        private async Task ApplySentNamesAsync(IReadOnlyList<HiwuCoflSampleRow> rows, CancellationToken cancellationToken)
        {
            var fileIds = rows.Select(row => row.FileId).Distinct().ToList();
            if (fileIds.Count == 0)
                return;

            var files = await _db.ImportFiles.AsNoTracking()
                .Where(file => fileIds.Contains(file.FileId))
                .Select(file => new { file.FileId, file.RawXml })
                .ToListAsync(cancellationToken);

            foreach (var file in files)
            {
                var fileRows = rows.Where(row => row.FileId == file.FileId).ToList();
                if (!HiwuManifestDocument.TryRead(file.RawXml, out var document))
                {
                    foreach (var row in fileRows)
                        row.SpecimenLabel = row.SampleKey;
                    continue;
                }

                for (var index = 0; index < document.Samples.Count; index++)
                {
                    var sample = document.Samples[index];
                    var key = SampleKey(sample, index);
                    var row = fileRows.FirstOrDefault(item => item.SampleKey == key);
                    if (row == null)
                        continue;
                    var externalRef = Field(sample.Fields, "externalRef");
                    row.AnimalLabel = externalRef.Length > 0 ? externalRef : sample.SampleId;
                    row.SpecimenLabel = string.IsNullOrWhiteSpace(sample.SampleId) ? key : sample.SampleId.Trim();
                }
            }
        }

        private static void AddRequired(HiwuCoflCatalog catalog, string name, long? id, string missing, List<string> problems)
        {
            if (catalog.LookupErrors.TryGetValue(name, out var error))
                problems.Add(error);
            else if (id == null)
                problems.Add(missing);
        }

        internal static string SampleKey(HiwuSampleDocument sample, int index)
        {
            var key = string.IsNullOrWhiteSpace(sample.SampleId) ? "sample-" + (index + 1) : sample.SampleId.Trim();
            return key.Length <= 100 ? key : key[..100];
        }

        internal static string AdditionalInfo(HiwuManifestDocument document)
        {
            var parts = new List<string>();
            var location = Field(document.Mission, "location");
            var shipped = Field(document.Mission, "dateShipped");
            var lockNumber = Field(document.Mission, "containerLockNumber");
            if (location.Length > 0)
                parts.Add("Location: " + location + ".");
            if (shipped.Length > 0)
                parts.Add("DateShipped: " + ReportDate(shipped));
            if (lockNumber.Length > 0)
                parts.Add("ContainerLockNum: " + lockNumber);
            return string.Join(" ", parts);
        }

        private static string ReportDate(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
                : value;
        }

        private static string Field(Dictionary<string, string> fields, string name)
        {
            return fields.TryGetValue(name, out var value) ? value.Trim() : "";
        }

        private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
