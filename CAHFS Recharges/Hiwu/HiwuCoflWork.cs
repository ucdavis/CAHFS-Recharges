namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuCoflWorkItem
    {
        public Guid FileId { get; init; }
        public string FileName { get; init; } = "";
        public DateTime ReceivedUtc { get; init; }
        public int SampleCount { get; init; }
        public bool HasBSample { get; init; }
        public string TraceFirstStatus { get; init; } = "";
        public string NextStep { get; init; } = "";
    }

    public sealed class HiwuCoflMapLine
    {
        public string HiwuField { get; init; } = "";
        public string TraceFirstField { get; init; } = "";
        public string Value { get; init; } = "";
    }

    public sealed class HiwuCoflSampleReview
    {
        public string SampleKey { get; init; } = "";
        public bool IsBSample { get; init; }
        public string Status { get; init; } = HiwuCoflSampleStatus.NotStarted;
        public long? AccessionId { get; init; }
        public string? AccessionNumber { get; init; }
        public long? AnimalId { get; init; }
        public string? AnimalLabel { get; init; }
        public long? SpecimenId { get; init; }
        public string? SpecimenLabel { get; init; }
        public string? ErrorMessage { get; init; }
        public IReadOnlyList<HiwuCoflMapLine> Lines { get; init; } = [];
    }

    public sealed class HiwuCoflWorkReview
    {
        public string TraceFirstStatus { get; init; } = HiwuCoflSampleStatus.NotStarted;
        public string? BlockReason { get; init; }
        public string Confirmation { get; init; } = "";
        public string ButtonLabel { get; init; } = "Create order";
        public bool CanSubmit { get; init; }
        public int PendingCount { get; init; }
        public int SkippedCount { get; init; }
        public long? AccessionId { get; init; }
        public IReadOnlyList<HiwuCoflMapLine> Accession { get; init; } = [];
        public IReadOnlyList<HiwuCoflSampleReview> Samples { get; init; } = [];

        public static HiwuCoflWorkReview Blocked(
            string status,
            IReadOnlyList<HiwuCoflSampleRow> saved,
            string reason)
        {
            return new HiwuCoflWorkReview
            {
                TraceFirstStatus = status,
                BlockReason = reason,
                AccessionId = saved.Select(row => row.AccessionId).FirstOrDefault(id => id != null),
                Samples = saved.Select(row => new HiwuCoflSampleReview
                {
                    SampleKey = row.SampleKey,
                    Status = row.Status,
                    AccessionId = row.AccessionId,
                    AccessionNumber = row.AccessionNumber,
                    AnimalId = row.AnimalId,
                    AnimalLabel = row.AnimalLabel,
                    SpecimenId = row.SpecimenId,
                    SpecimenLabel = row.SpecimenLabel,
                    ErrorMessage = row.ErrorMessage
                }).ToList()
            };
        }

        public static HiwuCoflWorkReview Build(
            HiwuManifestDocument document,
            IReadOnlyList<HiwuCoflSampleRow> saved,
            HiwuCoflCatalog catalog,
            string status)
        {
            var problems = new List<string>();
            var accession = new List<HiwuCoflMapLine>
            {
                Line("Lab", "Lab", Required(catalog, HiwuCoflCatalog.Labs, catalog.LabId, "Equine Analytical Chemistry", "Lab Equine Analytical Chemistry was not found.", problems)),
                Line("Client", "Client", Required(catalog, HiwuCoflCatalog.Clients, catalog.ClientId, "Horseracing Integrity and Welfare Unit (HIWU)", "Client Horseracing Integrity and Welfare Unit (HIWU) was not found.", problems)),
                Line("Breed", "Breed", Required(catalog, HiwuCoflCatalog.Breeds, catalog.BreedId, "Equidae :: Horse :: NFS", "Breed Equidae :: Horse :: NFS was not found.", problems))
            };

            var courier = Field(document.Mission, "courier");
            accession.Add(Line("Courier", "Carrier", catalog.DescribeCarrier(courier)));
            if (catalog.LookupErrors.TryGetValue(HiwuCoflCatalog.CarriersList, out var carrierError))
                problems.Add(carrierError);
            accession.Add(Line("Tracking ref", "Carrier reference", Display(Field(document.Mission, "trackingRef"))));
            accession.Add(Line(
                "Location, date shipped, container lock number",
                "Additional info",
                Display(HiwuCoflFileOrderService.AdditionalInfo(document))));

            var samples = new List<HiwuCoflSampleReview>();
            var pending = 0;
            var skipped = 0;
            for (var index = 0; index < document.Samples.Count; index++)
            {
                var sample = document.Samples[index];
                var key = HiwuCoflFileOrderService.SampleKey(sample, index);
                var existing = saved.FirstOrDefault(row => row.SampleKey == key);
                var imported = existing?.Status == HiwuCoflSampleStatus.Imported;
                if (imported)
                    skipped++;
                else
                    pending++;

                List<string> sampleProblems = imported ? new List<string>() : problems;
                samples.Add(new HiwuCoflSampleReview
                {
                    SampleKey = key,
                    IsBSample = HiwuManifestDocument.IsBSampleRequested(sample),
                    Status = existing?.Status ?? HiwuCoflSampleStatus.NotStarted,
                    AccessionId = existing?.AccessionId,
                    AccessionNumber = existing?.AccessionNumber,
                    AnimalId = existing?.AnimalId,
                    AnimalLabel = existing?.AnimalLabel,
                    SpecimenId = existing?.SpecimenId,
                    SpecimenLabel = existing?.SpecimenLabel,
                    ErrorMessage = existing?.ErrorMessage,
                    Lines = SampleLines(sample, key, catalog, sampleProblems)
                });
            }

            var accessionId = saved.Select(row => row.AccessionId).FirstOrDefault(id => id != null);
            var canSubmit = problems.Count == 0 && pending > 0;
            return new HiwuCoflWorkReview
            {
                TraceFirstStatus = status,
                BlockReason = problems.Count == 0 ? null : string.Join(" ", problems.Distinct()),
                Confirmation = ConfirmationText(accessionId, pending, skipped),
                ButtonLabel = accessionId == null ? "Create order" : "Continue order",
                CanSubmit = canSubmit,
                PendingCount = pending,
                SkippedCount = skipped,
                AccessionId = accessionId,
                Accession = accession,
                Samples = samples
            };
        }

        private static IReadOnlyList<HiwuCoflMapLine> SampleLines(
            HiwuSampleDocument sample,
            string sampleKey,
            HiwuCoflCatalog catalog,
            List<string> problems)
        {
            var type = Field(sample.Fields, "sampleType");
            var point = Field(sample.Fields, "collectionPoint");
            var gender = Field(sample.Fields, "gender");
            var description = HiwuCoflSampleMapper.SpecimenDescription(sample, point);
            var skipTests = HiwuManifestDocument.IsBSampleRequested(sample);
            var sexFailed = catalog.LookupErrors.TryGetValue(HiwuCoflCatalog.SexCodesList, out var sexError);
            var typeFailed = catalog.LookupErrors.TryGetValue(HiwuCoflCatalog.SpecimenTypesList, out var typeError);
            var descriptionFailed = catalog.LookupErrors.TryGetValue(HiwuCoflCatalog.SpecimenDescriptionsList, out var descriptionError);
            var testsLookupFailed = catalog.LookupErrors.TryGetValue(HiwuCoflCatalog.TestSchedulesList, out var testsError);
            var testsFailed = !skipTests && testsLookupFailed;
            var sexId = sexFailed ? null : HiwuCoflCatalog.Match(catalog.SexCodes, gender);
            var specimenTypeId = typeFailed ? null : HiwuCoflCatalog.Match(catalog.SpecimenTypes, type);
            var descriptionId = descriptionFailed ? null : HiwuCoflCatalog.MatchForType(catalog.SpecimenDescriptions, description, specimenTypeId, catalog.SpecimenTypes);
            var panels = skipTests ? Array.Empty<string>() : HiwuCoflSampleMapper.Panels(type, point);
            var tests = new List<string>();
            if (testsFailed)
                tests.Add(testsError!);
            else if (!skipTests)
            {
                foreach (var panel in panels)
                {
                    var id = catalog.MatchPanel(panel);
                    if (id == null)
                    {
                        tests.Add(panel + " was not found.");
                        problems.Add(sampleKey + " test schedule " + panel + " was not found.");
                    }
                    else
                    {
                        tests.Add(panel + " (" + id + ")");
                    }
                }
            }

            if (sexFailed)
                problems.Add(sexError!);
            else if (sexId == null)
                problems.Add(sampleKey + " sex " + gender + " was not found.");
            if (typeFailed)
                problems.Add(typeError!);
            else if (specimenTypeId == null)
                problems.Add(sampleKey + " specimen type " + type + " was not found.");
            if (descriptionFailed)
                problems.Add(descriptionError!);
            else if (descriptionId == null)
                problems.Add(sampleKey + " specimen description " + description + " was not found.");
            if (testsFailed)
                problems.Add(testsError!);

            var externalRef = Field(sample.Fields, "externalRef");
            var animalName = string.IsNullOrWhiteSpace(externalRef) ? sample.SampleId : externalRef;
            var testScheduleValue = skipTests
                ? "Tests will not be assigned (B-Sample)."
                : tests.Count == 0
                    ? "Tests will not be assigned."
                    : string.Join(", ", tests);

            return
            [
                Line(string.IsNullOrWhiteSpace(externalRef) ? "Sample id" : "External ref", "Animal tag", catalog.NameTagTypeId == null ? "Not sent. " + (catalog.LookupErrors.TryGetValue("Tag types", out var tagError) ? tagError : "Tag type NAME was not found.") : "NAME (" + catalog.NameTagTypeId + ") " + animalName),
                Line("Gender", "Sex", sexFailed ? sexError! : sexId == null ? gender + " was not found." : gender + " (" + sexId + ")"),
                Line("Age", "Age", Display(Field(sample.Fields, "age"))),
                Line("Age unit", "Age unit", Required(catalog, HiwuCoflCatalog.AgeUnits, catalog.AgeUomId, "Years", "Age unit Years was not found.", problems)),
                Line("Sample id", "Specimen name", Display(sample.SampleId)),
                Line("Sample type", "Specimen type", typeFailed ? typeError! : specimenTypeId == null ? type + " was not found." : type + " (" + specimenTypeId + ")"),
                Line("Collection point", "Specimen description", descriptionFailed ? descriptionError! : descriptionId == null ? description + " was not found." : description + " (" + descriptionId + ")"),
                Line("Sampled date", "Collection date", Display(Field(sample.Fields, "sampledDate"))),
                Line("Optional comment", "Case coordinator comment", Display(Field(sample.Fields, "optionalComment"))),
                Line("Collection point and sample type", "Test schedule", testScheduleValue)
            ];
        }

        private static string ConfirmationText(long? accessionId, int pending, int skipped)
        {
            if (pending == 0)
                return "";

            var remaining = pending == 1 ? "1 sample" : pending + " samples";
            var already = skipped == 1 ? "1 sample is" : skipped + " samples are";
            return accessionId == null
                ? "This creates 1 accession and adds " + remaining + ". " + already + " already in TraceFirst and will be skipped."
                : "This adds " + remaining + " to accession " + accessionId + ". " + already + " already in TraceFirst and will be skipped.";
        }

        private static string Required(
            HiwuCoflCatalog catalog,
            string listName,
            long? id,
            string name,
            string missing,
            List<string> problems)
        {
            if (catalog.LookupErrors.TryGetValue(listName, out var error))
            {
                problems.Add(error);
                return error;
            }

            return Named(id, name, missing, problems);
        }

        private static string Named(long? id, string name, string missing, List<string> problems)
        {
            if (id != null)
                return name + " (" + id + ")";
            problems.Add(missing);
            return missing;
        }

        private static HiwuCoflMapLine Line(string hiwu, string traceFirst, string value) =>
            new() { HiwuField = hiwu, TraceFirstField = traceFirst, Value = value };

        private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

        private static string Field(Dictionary<string, string> fields, string name) =>
            fields.TryGetValue(name, out var value) ? value.Trim() : "";
    }
}
