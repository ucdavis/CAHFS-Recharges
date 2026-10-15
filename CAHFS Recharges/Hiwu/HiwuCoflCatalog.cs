namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuCoflCatalog
    {
        public long? LabId { get; init; }
        public long? ClientId { get; init; }
        public long? BreedId { get; init; }
        public long? AgeUomId { get; init; }
        public long? NameTagTypeId { get; init; }
        public IReadOnlyList<HiwuCoflNamedRecord> Carriers { get; init; } = [];
        public IReadOnlyList<HiwuCoflNamedRecord> SexCodes { get; init; } = [];
        public IReadOnlyList<HiwuCoflNamedRecord> SpecimenTypes { get; init; } = [];
        public IReadOnlyList<HiwuCoflNamedRecord> SpecimenDescriptions { get; init; } = [];
        public IReadOnlyList<HiwuCoflNamedRecord> TestSchedules { get; init; } = [];
        public IReadOnlyDictionary<string, long> ScheduleIds { get; init; } = new Dictionary<string, long>();
        public IReadOnlyDictionary<string, string> LookupErrors { get; init; } = new Dictionary<string, string>();

        public const string Labs = "Labs";
        public const string Clients = "Clients";
        public const string Breeds = "Breeds";
        public const string AgeUnits = "Age units";
        public const string CarriersList = "Carriers";
        public const string SexCodesList = "Sex codes";
        public const string SpecimenTypesList = "Specimen types";
        public const string SpecimenDescriptionsList = "Specimen descriptions";
        public const string TestSchedulesList = "Test schedules";

        public static async Task<HiwuCoflCatalog> LoadAsync(
            HiwuCoflClient client,
            IReadOnlyDictionary<string, long> scheduleIds,
            CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, string>();
            var labs = await ListAsync(client, Labs, "api/v2/labs", "api/v1/labs", errors, cancellationToken);
            var clients = await ListAsync(client, Clients, "api/v2/clients", "api/v1/clients", errors, cancellationToken);
            var breeds = await ListAsync(client, Breeds, "api/v2/breeds", "api/v1/breeds", errors, cancellationToken);
            var uoms = await ListAsync(client, AgeUnits, "api/v2/uoms", "api/v1/uoms", errors, cancellationToken);
            var carriers = await ListAsync(client, CarriersList, "api/v2/carriers", "api/v1/carriers", errors, cancellationToken);
            var sexCodes = await ListAsync(client, SexCodesList, "api/v2/sex_codes", "api/v1/sex_codes", errors, cancellationToken);
            var specimenTypes = await ListAsync(client, SpecimenTypesList, "api/v2/specimen_types", "api/v1/specimen_types", errors, cancellationToken);
            var descriptions = await ListAsync(client, SpecimenDescriptionsList, "api/v2/specimen_type_descriptions", "api/v1/specimen_type_descriptions", errors, cancellationToken);
            var schedules = await ListAsync(client, TestSchedulesList, "api/v2/test_schedules", "api/v1/test_schedules", errors, cancellationToken);
            var resolved = new Dictionary<string, long>(scheduleIds, StringComparer.OrdinalIgnoreCase);
            if (!errors.ContainsKey(TestSchedulesList))
            {
                foreach (var code in PanelCodes)
                {
                    if (resolved.TryGetValue(code, out var configured) && configured > 0)
                        continue;
                    var listed = MatchCode(schedules, code);
                    if (listed != null)
                    {
                        resolved[code] = ToScheduleId(schedules, listed.Value);
                        continue;
                    }

                    ScheduleLongNames.TryGetValue(code, out var longName);
                    var id = await client.FindTestScheduleIdAsync(code, longName, cancellationToken);
                    if (id != null)
                        resolved[code] = ToScheduleId(schedules, id.Value);
                }
            }

            return new HiwuCoflCatalog
            {
                LabId = Match(labs, "Equine Analytical Chemistry"),
                ClientId = Match(clients, "Horseracing Integrity and Welfare Unit (HIWU)"),
                BreedId = MatchBreed(breeds, "Equidae :: Horse :: NFS"),
                AgeUomId = Match(uoms, "Years"),
                NameTagTypeId = 16,
                Carriers = carriers,
                SexCodes = sexCodes,
                SpecimenTypes = specimenTypes,
                SpecimenDescriptions = descriptions,
                TestSchedules = schedules,
                ScheduleIds = resolved,
                LookupErrors = errors
            };
        }

        private static readonly string[] PanelCodes = ["E501", "E508", "E509", "E600", "E602", "E603", "E604", "E606"];

        public long? MatchPanel(string code)
        {
            if (ScheduleIds.TryGetValue(code, out var configured) && configured > 0)
                return ToScheduleId(TestSchedules, configured);

            var marked = TestSchedules.Where(record => record.IsTestSchedule).ToList();
            var matched = MatchCode(marked, code) ?? MatchCode(TestSchedules, code);
            return matched == null ? null : ToScheduleId(TestSchedules, matched.Value);
        }

        private static long ToScheduleId(IReadOnlyList<HiwuCoflNamedRecord> records, long id)
        {
            var schedules = records.Where(record => record.TestId == id && !record.Archived && !record.Deleted).ToList();
            if (schedules.Count == 0)
                return id;
            return schedules.OrderBy(record => record.Id).First().Id;
        }

        private static async Task<IReadOnlyList<HiwuCoflNamedRecord>> ListAsync(
            HiwuCoflClient client,
            string name,
            string v2Path,
            string v1Path,
            Dictionary<string, string> errors,
            CancellationToken cancellationToken)
        {
            if (errors.Values.Any(error => error.Contains("login", StringComparison.OrdinalIgnoreCase)))
                return [];

            try
            {
                return await client.ListNamedAsync(v2Path, v1Path, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                errors[name] = name + ": " + ex.Message;
                return [];
            }
        }

        public long? MatchCarrier(string courier)
        {
            var wanted = Letters(courier);
            if (wanted.Length == 0)
                return null;

            var exact = Carriers.Where(carrier => carrier.Names.Any(name => Letters(name) == wanted)).ToList();
            if (exact.Count == 1)
                return exact[0].Id;
            if (exact.Count > 1)
                return null;

            var close = Carriers.Where(carrier => carrier.Names.Any(name => Distance(Letters(name), wanted) == 1)).ToList();
            return close.Count == 1 ? close[0].Id : null;
        }

        public static long? Match(IReadOnlyList<HiwuCoflNamedRecord> records, string expected)
        {
            var wanted = Fold(expected);
            var matches = records.Where(record => record.Names.Any(name => Fold(name) == wanted)).ToList();
            if (matches.Count == 1)
                return matches[0].Id;
            if (matches.Count > 1)
                return null;

            return MatchCompact(records, expected);
        }

        public static long? MatchCode(IReadOnlyList<HiwuCoflNamedRecord> records, string code)
        {
            var wanted = Fold(code);
            var compact = Compact(code);
            var exact = records.Where(record => record.Names.Any(name => Fold(name) == wanted || Compact(name) == compact)).ToList();
            if (exact.Count == 1)
                return exact[0].Id;
            if (exact.Count > 1)
                return exact.OrderByDescending(record => record.Names.Count).ThenBy(record => record.Id).First().Id;

            if (ScheduleLongNames.TryGetValue(code, out var longName))
            {
                var byLongName = Match(records, longName) ?? MatchCompact(records, longName);
                if (byLongName != null)
                    return byLongName;

                var combined = Compact(code + longName);
                var bySearchText = records.Where(record => record.Names.Any(name => Compact(name) == combined)).ToList();
                if (bySearchText.Count == 1)
                    return bySearchText[0].Id;
            }

            var matches = records.Where(record => record.Names.Any(name => HasCode(name, code))).ToList();
            return matches.Count == 1 ? matches[0].Id : null;
        }

        private static readonly Dictionary<string, string> ScheduleLongNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["E500"] = "EACL Polar Drugs (LC-MS)",
            ["E501"] = "EACL Bisphosphoante Drugs (LC-MS)",
            ["E502"] = "EACL Anabolic Steroids in Urine (LC-MS)",
            ["E503"] = "EACL ITPP (LC-MS)",
            ["E504"] = "EACL Anabolic Steroids in Blood (LC-MS)",
            ["E505"] = "EACL Beta Agonists in Hair (LC-MS)",
            ["E506"] = "EACL Broad Coverage Hair Screen (LC-MS)",
            ["E507"] = "EACL Anabolic Steroids in Hair (LC-MS)",
            ["E508"] = "EACL Total Carbon Dioxide (GC-MS)",
            ["E509"] = "Cobalt Analysis",
            ["E510"] = "EACL EPO (ELISA)",
            ["E511"] = "EACL IGF1 (ELISA)",
            ["E512"] = "EACL Acid/Neutral Drugs (LC-MS)",
            ["E513"] = "EACL Basic Drug Screen (LC-MS)",
            ["E514"] = "EACL Broad Coverage Screen (LC-MS)",
            ["E515"] = "EACL Quaternary Drug Screen (LC-MS)",
            ["E516"] = "EACL Volatile Substances (GC-MS)",
            ["E517"] = "EACL Confirmation Testing",
            ["E518"] = "EACL Peptide Screening (LC-MS)",
            ["E519"] = "EACL Beta Agonists Screening (LC-MS)",
            ["E520"] = "EACL Beta Agonist Hair Screening - low level (LC-MS)",
            ["E521"] = "EACL Specific Gravity",
            ["E522"] = "EACL pH",
            ["E527"] = "EACL Threshold Anabolic Steroids in Urine (LC-MS)",
            ["E600"] = "Urine Post-Competition - Paired",
            ["E601"] = "Blood Post-Competition - Paired",
            ["E602"] = "Blood Only Post-Competition",
            ["E603"] = "Out of Competition Testing",
            ["E604"] = "Hair Testing",
            ["E605"] = "Cobalt/EPO"
        };

        private static string Compact(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private static bool SameValue(string left, string right)
        {
            return Fold(left) == Fold(right) || Compact(left) == Compact(right);
        }

        public static long? MatchBreed(IReadOnlyList<HiwuCoflNamedRecord> records, string expected)
        {
            var direct = Match(records, expected);
            if (direct != null)
                return direct;

            var wanted = Fold(expected);
            var byId = records.ToDictionary(record => record.Id);
            var matches = records.Where(record => Fold(BreedPath(record, byId)) == wanted).ToList();
            if (matches.Count == 1)
                return matches[0].Id;
            if (matches.Count > 1)
                return null;

            var compact = Compact(expected);
            matches = records.Where(record => Compact(BreedPath(record, byId)) == compact).ToList();
            return matches.Count == 1 ? matches[0].Id : null;
        }

        public static long? MatchForType(
            IReadOnlyList<HiwuCoflNamedRecord> descriptions,
            string expected,
            long? specimenTypeId,
            IReadOnlyList<HiwuCoflNamedRecord> specimenTypes)
        {
            var wanted = Fold(expected);
            var exact = MatchForType(descriptions, specimenTypeId, specimenTypes, name => Fold(name) == wanted);
            if (exact != null)
                return exact;

            var compact = Compact(expected);
            return MatchForType(descriptions, specimenTypeId, specimenTypes, name => Compact(name) == compact);
        }

        private static long? MatchForType(
            IReadOnlyList<HiwuCoflNamedRecord> descriptions,
            long? specimenTypeId,
            IReadOnlyList<HiwuCoflNamedRecord> specimenTypes,
            Func<string, bool> nameMatches)
        {
            var named = descriptions.Where(record => record.Names.Any(nameMatches)).ToList();
            if (named.Count == 1)
                return named[0].Id;

            var typeNames = specimenTypes.FirstOrDefault(record => record.Id == specimenTypeId)?.Names ?? [];
            var typed = named.Where(record =>
                (specimenTypeId != null && record.SpecimenTypeId == specimenTypeId)
                || (record.SpecimenTypeName != null && typeNames.Any(name => SameValue(name, record.SpecimenTypeName)))).ToList();
            return typed.Count == 1 ? typed[0].Id : null;
        }

        private static long? MatchCompact(IReadOnlyList<HiwuCoflNamedRecord> records, string expected)
        {
            var wanted = Compact(expected);
            var matches = records.Where(record => record.Names.Any(name => Compact(name) == wanted)).ToList();
            return matches.Count == 1 ? matches[0].Id : null;
        }

        private static string BreedPath(HiwuCoflNamedRecord record, Dictionary<long, HiwuCoflNamedRecord> byId)
        {
            var names = new List<string>();
            var seen = new HashSet<long>();
            HiwuCoflNamedRecord? current = record;
            while (current != null && seen.Add(current.Id) && names.Count < 8)
            {
                if (!string.IsNullOrWhiteSpace(current.Name))
                    names.Add(current.Name);
                current = current.ParentId is long parentId && byId.TryGetValue(parentId, out var parent) ? parent : null;
            }

            names.Reverse();
            return string.Join(" :: ", names);
        }

        public static string Fold(string value)
        {
            var parts = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', parts).ToLowerInvariant();
        }

        public static string Letters(string value)
        {
            return new string(value.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
        }

        private static bool HasCode(string name, string code)
        {
            var parts = name.Split([' ', '-', '/', '_', '(', ')', ',', ':'], StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(part => string.Equals(part, code, StringComparison.OrdinalIgnoreCase));
        }

        private static int Distance(string left, string right)
        {
            if (Math.Abs(left.Length - right.Length) > 1)
                return 2;

            var changes = 0;
            var i = 0;
            var j = 0;
            while (i < left.Length && j < right.Length)
            {
                if (left[i] == right[j])
                {
                    i++;
                    j++;
                    continue;
                }

                changes++;
                if (changes > 1)
                    return 2;
                if (left.Length > right.Length)
                    i++;
                else if (right.Length > left.Length)
                    j++;
                else
                {
                    i++;
                    j++;
                }
            }

            if (i < left.Length || j < right.Length)
                changes++;
            return changes;
        }
    }

    public static class HiwuCoflSampleMapper
    {
        public static (HiwuCoflOrderSample? Sample, string? Error) Map(
            HiwuSampleDocument sample,
            string sampleKey,
            HiwuCoflCatalog catalog,
            HiwuCoflSampleRow? existing)
        {
            var type = Field(sample, "sampleType");
            var point = Field(sample, "collectionPoint");
            if (catalog.LookupErrors.ContainsKey(HiwuCoflCatalog.SexCodesList)
                || catalog.LookupErrors.ContainsKey(HiwuCoflCatalog.SpecimenTypesList)
                || catalog.LookupErrors.ContainsKey(HiwuCoflCatalog.SpecimenDescriptionsList)
                || catalog.LookupErrors.ContainsKey(HiwuCoflCatalog.TestSchedulesList)
                || catalog.LookupErrors.ContainsKey(HiwuCoflCatalog.Breeds)
                || catalog.LookupErrors.ContainsKey(HiwuCoflCatalog.AgeUnits))
                return (null, null);

            var gender = Field(sample, "gender");
            var sexId = HiwuCoflCatalog.Match(catalog.SexCodes, gender);
            if (sexId == null)
                return (null, sampleKey + " sex " + gender + " was not found.");

            var specimenTypeId = HiwuCoflCatalog.Match(catalog.SpecimenTypes, type);
            if (specimenTypeId == null)
                return (null, sampleKey + " specimen type " + type + " was not found.");

            var description = point + (Lasix(sample) ? " With Lasix" : "");
            var descriptionId = HiwuCoflCatalog.MatchForType(catalog.SpecimenDescriptions, description, specimenTypeId, catalog.SpecimenTypes);
            if (descriptionId == null)
                return (null, sampleKey + " specimen description " + description + " was not found.");

            var panels = Panels(type, point);
            var testIds = new List<long>();
            foreach (var panel in panels)
            {
                var id = catalog.MatchPanel(panel);
                if (id == null)
                    return (null, sampleKey + " test schedule " + panel + " was not found.");
                testIds.Add(id.Value);
            }

            var externalRef = Field(sample, "externalRef");
            var animalName = string.IsNullOrWhiteSpace(externalRef) ? sample.SampleId : externalRef;

            return (new HiwuCoflOrderSample
            {
                SampleKey = sampleKey,
                AnimalId = existing?.AnimalId,
                SpecimenId = existing?.SpecimenId,
                Animal = existing?.AnimalId == null
                    ? new HiwuCoflAnimalRequest
                    {
                        BreedId = catalog.BreedId,
                        SexCodeId = sexId,
                        Age = Field(sample, "age"),
                        AgeUomId = catalog.AgeUomId,
                        Name = animalName,
                        Tags = catalog.NameTagTypeId == null || string.IsNullOrWhiteSpace(animalName)
                            ? []
                            : [new HiwuCoflAnimalTag { TagTypeId = catalog.NameTagTypeId.Value, Num = animalName }]
                    }
                    : null,
                Specimen = existing?.SpecimenId == null
                    ? new HiwuCoflSpecimenRequest
                    {
                        SpecimenTypeId = specimenTypeId,
                        SpecimenTypeDescriptionId = descriptionId,
                        CollectionDate = Field(sample, "sampledDate"),
                        Name = sample.SampleId,
                        CaseCoordinatorComment = EmptyToNull(Field(sample, "optionalComment"))
                    }
                    : null,
                TestScheduleIds = testIds
            }, null);
        }

        public static IReadOnlyList<string> Panels(string sampleType, string collectionPoint)
        {
            var type = sampleType.Trim().ToUpperInvariant();
            var point = collectionPoint.Trim().ToUpperInvariant();
            if (type == "HAIR")
                return ["E604"];
            if (type == "URINE" && point == "POST RACE")
                return ["E600"];
            if (type == "BLOOD" && point == "POST RACE")
                return ["E602"];
            if (type == "BLOOD" && point == "TCO2")
                return ["E508"];
            if (type == "BLOOD" && point == "OOC")
                return ["E603"];
            if (type == "BLOOD" && point is "VETS LIST" or "CLAIMED" or "POST WORK")
                return ["E602"];
            if (type == "BLOOD" && point == "FATALITY/INJURY - POST RACE")
                return ["E602", "E501", "E509"];
            if (type == "BLOOD" && point == "FATALITY/INJURY - OTHER")
                return ["E602", "E501"];
            if (type == "BLOOD" && point == "OTHER")
                return ["E606"];
            if (type == "OTHER" && point == "EVIDENCE")
                return ["E606"];
            return [];
        }

        private static bool Lasix(HiwuSampleDocument sample)
        {
            var value = Field(sample, "lasix");
            return value is "1" or "true";
        }

        private static string Field(HiwuSampleDocument sample, string name)
        {
            return sample.Fields.TryGetValue(name, out var value) ? value.Trim() : "";
        }

        private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
