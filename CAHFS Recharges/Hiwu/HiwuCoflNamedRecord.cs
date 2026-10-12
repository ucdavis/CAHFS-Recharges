using System.Text.Json;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuCoflNamedRecord
    {
        public long Id { get; init; }
        public string? Name { get; init; }
        public long? ParentId { get; init; }
        public long? SpecimenTypeId { get; init; }
        public string? SpecimenTypeName { get; init; }
        public long? TestId { get; init; }
        public long? LabId { get; init; }
        public bool IsTestSchedule { get; init; }
        public IReadOnlyList<string> Names { get; init; } = [];

        public HiwuCoflNamedRecord WithNames(IEnumerable<string> extra)
        {
            var names = Names.ToList();
            foreach (var name in extra)
            {
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }

            return new HiwuCoflNamedRecord
            {
                Id = Id,
                Name = Name,
                ParentId = ParentId,
                SpecimenTypeId = SpecimenTypeId,
                SpecimenTypeName = SpecimenTypeName,
                TestId = TestId,
                LabId = LabId,
                IsTestSchedule = IsTestSchedule,
                Names = names
            };
        }

        public static long? PickListedId(string? json, string code, string? longName)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                using var document = JsonDocument.Parse(json);
                var items = ListItems(document.RootElement);
                var hits = new List<long>();
                foreach (var item in items)
                {
                    if (!TryItemId(item, out var id))
                        continue;
                    if (ContainsText(item, code, longName))
                        hits.Add(id);
                }

                var distinct = hits.Distinct().ToList();
                if (distinct.Count == 1)
                    return distinct[0];
                if (items.Count == 1 && TryItemId(items[0], out var onlyId))
                    return onlyId;
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static List<JsonElement> ListItems(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
                return root.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList();
            if (root.ValueKind != JsonValueKind.Object)
                return [];

            JsonElement? best = null;
            var bestCount = 0;
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                    continue;
                var count = 0;
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        count++;
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    best = property.Value;
                }
            }

            if (best == null)
                return [root];
            return best.Value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList();
        }

        private static bool TryItemId(JsonElement item, out long id)
        {
            if (TryScheduleId(item, out id, out _))
                return true;

            foreach (var property in item.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object
                    && TryScheduleId(property.Value, out id, out var isSchedule)
                    && isSchedule)
                    return true;
            }

            foreach (var property in item.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object && TryId(property.Value, out id))
                    return true;
            }

            id = 0;
            return false;
        }

        private static bool ContainsText(JsonElement element, string code, string? longName)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString()?.Trim();
                return !string.IsNullOrEmpty(text) && TextMatches(text, code, longName);
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (ContainsText(property.Value, code, longName))
                        return true;
                }
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsText(item, code, longName))
                        return true;
                }
            }

            return false;
        }

        private static bool TextMatches(string text, string code, string? longName)
        {
            if (SameText(text, code) || (!string.IsNullOrWhiteSpace(longName) && SameText(text, longName)))
                return true;

            var parts = text.Split([' ', '-', '/', '_', '(', ')', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(part => SameText(part, code)))
                return true;

            if (string.IsNullOrWhiteSpace(longName))
                return false;

            var compact = LettersAndDigits(text);
            return compact == LettersAndDigits(code + longName)
                || (LettersAndDigits(longName).Length >= 8 && compact.Contains(LettersAndDigits(longName), StringComparison.Ordinal));
        }

        private static bool SameText(string text, string expected)
        {
            if (text.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return true;
            return LettersAndDigits(text) == LettersAndDigits(expected);
        }

        private static string LettersAndDigits(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        public static IReadOnlyList<HiwuCoflNamedRecord> Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                using var document = JsonDocument.Parse(json);
                var found = new Dictionary<long, NamedFields>();
                Walk(document.RootElement, found, null);
                return found.Select(pair => new HiwuCoflNamedRecord
                {
                    Id = pair.Key,
                    Name = pair.Value.Name,
                    ParentId = pair.Value.ParentId,
                    SpecimenTypeId = pair.Value.SpecimenTypeId,
                    SpecimenTypeName = pair.Value.SpecimenTypeName,
                    TestId = pair.Value.TestId,
                    LabId = pair.Value.LabId,
                    IsTestSchedule = pair.Value.IsTestSchedule,
                    Names = pair.Value.Names
                }).ToList();
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static void Walk(JsonElement element, Dictionary<long, NamedFields> found, NamedFields? parent)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                NamedFields? current = parent;
                if (TryScheduleId(element, out var id, out var isSchedule))
                {
                    if (!found.TryGetValue(id, out var fields))
                    {
                        fields = new NamedFields();
                        found[id] = fields;
                    }

                    current = fields;
                    if (isSchedule)
                        fields.IsTestSchedule = true;
                    ReadNames(element, fields);
                    ReadLinks(element, fields);
                }
                else if (parent != null)
                {
                    ReadNames(element, parent);
                }

                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, found, current);
                return;
            }

            if (element.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in element.EnumerateArray())
                Walk(item, found, parent);
        }

        private static void ReadNames(JsonElement element, NamedFields fields)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    continue;

                var text = property.Value.GetString()?.Trim()?.Trim('\u00a0');
                if (string.IsNullOrEmpty(text) || text.Length > 120 || fields.Names.Contains(text, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (text.Contains("://", StringComparison.Ordinal) || text.Contains('@'))
                    continue;
                var key = property.Name;
                fields.Names.Add(text);
                if (key == "name" && string.IsNullOrEmpty(fields.Name))
                    fields.Name = text;
                if (key is "specimen_type_name" or "specimen_type_short_name")
                    fields.SpecimenTypeName = text;
            }
        }

        private static void ReadLinks(JsonElement element, NamedFields fields)
        {
            if (TryId(element, "specimen_type_id", out var specimenTypeId))
                fields.SpecimenTypeId = specimenTypeId;
            if (element.TryGetProperty("specimen_type", out var specimenType))
            {
                if (specimenType.ValueKind == JsonValueKind.String)
                    fields.SpecimenTypeName = specimenType.GetString()?.Trim();
                else if (specimenType.ValueKind == JsonValueKind.Object)
                {
                    if (TryId(specimenType, out var nestedTypeId))
                        fields.SpecimenTypeId = nestedTypeId;
                    if (specimenType.TryGetProperty("name", out var typeName) && typeName.ValueKind == JsonValueKind.String)
                        fields.SpecimenTypeName = typeName.GetString()?.Trim();
                    else if (specimenType.TryGetProperty("short_name", out var shortType) && shortType.ValueKind == JsonValueKind.String)
                        fields.SpecimenTypeName = shortType.GetString()?.Trim();
                }
            }

            if (TryId(element, "test_id", out var testId))
                fields.TestId = testId;
            else if (element.TryGetProperty("test", out var test)
                && test.ValueKind == JsonValueKind.Object
                && TryId(test, out var nestedTestId))
                fields.TestId = nestedTestId;

            if (TryId(element, "lab_id", out var labId))
                fields.LabId = labId;

            if (TryId(element, "parent_id", out var parentId))
                fields.ParentId = parentId;
            else if (element.TryGetProperty("parent", out var parent)
                && parent.ValueKind == JsonValueKind.Object
                && TryId(parent, out var nestedParentId))
                fields.ParentId = nestedParentId;
        }

        private static bool TryScheduleId(JsonElement element, out long id, out bool isSchedule)
        {
            if (TryId(element, "test_schedule_id", out id))
            {
                isSchedule = true;
                return true;
            }

            isSchedule = false;
            if (TryId(element, out id))
                return true;
            return TryId(element, "tag_type_id", out id);
        }

        private static bool TryId(JsonElement element, out long id) => TryId(element, "id", out id);

        private static bool TryId(JsonElement element, string name, out long id)
        {
            id = 0;
            if (!element.TryGetProperty(name, out var property))
                return false;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out id))
                return true;
            return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out id);
        }

        private sealed class NamedFields
        {
            public string? Name { get; set; }
            public long? ParentId { get; set; }
            public long? SpecimenTypeId { get; set; }
            public string? SpecimenTypeName { get; set; }
            public long? TestId { get; set; }
            public long? LabId { get; set; }
            public bool IsTestSchedule { get; set; }
            public List<string> Names { get; } = [];
        }
    }
}
