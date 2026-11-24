using System.Xml.Linq;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuFieldCompare
    {
        public string Field { get; init; } = "";
        public string Original { get; init; } = "";
        public string Amendment { get; init; } = "";
        public bool Changed { get; init; }
    }

    public sealed class HiwuSampleCompare
    {
        public string SampleId { get; init; } = "";
        public bool MissingOnOriginal { get; init; }
        public List<HiwuFieldCompare> Fields { get; init; } = new();
    }

    public sealed class HiwuSampleDocument
    {
        public string SampleId { get; set; } = "";

        public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);
    }

    public sealed class HiwuManifestDocument
    {
        /// Preferred mission column order (v2.0 additive; extras append dynamically).
        public static readonly string[] MissionFields =
        [
            "location", "trackingRef", "courier", "dateShipped", "containerLockNumber", "targetAnalytes"
        ];

        /// Preferred sample column order from Samples XSD v2.0; unknown keys append dynamically.
        public static readonly string[] SampleFields =
        [
            "sampleRef", "sampledDate", "collectionPoint", "age", "gender", "lasix", "sampleType",
            "longTermStorage", "discardDate", "externalRef", "sampleId", "numberOfContainers",
            "optionalComment", "expeditedAnalysis", "BSampleTestRequested", "BSampleTestNotes",
            "duplicateTCO2SampleRequested", "hairType", "hairCollectionMethod", "segmentalAnalysisRequested",
            "medicationsAdministered", "collectedBeforeDeath"
        ];

        public static readonly string[] BooleanFields =
        [
            "lasix", "longTermStorage", "expeditedAnalysis", "BSampleTestRequested",
            "duplicateTCO2SampleRequested", "segmentalAnalysisRequested", "collectedBeforeDeath"
        ];

        public Dictionary<string, string> Mission { get; } = new(StringComparer.Ordinal);

        public List<HiwuSampleDocument> Samples { get; } = new();

        public static bool TryRead(string? xml, out HiwuManifestDocument document)
        {
            document = new HiwuManifestDocument();
            if (string.IsNullOrWhiteSpace(xml))
                return false;

            try
            {
                var root = XDocument.Parse(xml.TrimStart('\uFEFF')).Root;
                if (root == null || !string.Equals(root.Name.LocalName, "testMission", StringComparison.Ordinal))
                    return false;

                foreach (var child in root.Elements())
                {
                    if (child.Name.LocalName == "samples")
                    {
                        foreach (var sample in child.Elements().Where(e => e.Name.LocalName == "sample"))
                            document.Samples.Add(ReadSample(sample));
                        continue;
                    }

                    document.Mission[child.Name.LocalName] = child.Value.Trim();
                }

                // Some amended payloads nest sample nodes differently; still count them for SampleCount/search.
                if (document.Samples.Count == 0)
                {
                    foreach (var sample in root.Descendants().Where(e => e.Name.LocalName == "sample"))
                        document.Samples.Add(ReadSample(sample));
                }

                return true;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        public static string Display(string field, string? value)
        {
            value = value?.Trim() ?? "";
            if (value.Length == 0)
                return "";

            if (BooleanFields.Contains(field, StringComparer.Ordinal))
            {
                if (value is "0" or "false")
                    return "No";
                if (value is "1" or "true")
                    return "Yes";
            }

            return value;
        }

        public static bool IsBSampleRequested(HiwuSampleDocument sample)
        {
            sample.Fields.TryGetValue("BSampleTestRequested", out var value);
            return string.Equals(Display("BSampleTestRequested", value), "Yes", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTransferCollection(HiwuSampleDocument sample)
        {
            sample.Fields.TryGetValue("collectionPoint", out var value);
            return string.Equals(value?.Trim(), "TRANSFER", StringComparison.OrdinalIgnoreCase);
        }

        /// B-Sample and Transfer: create animal/specimen only — no test schedules.
        public static bool SkipTestAssignment(HiwuSampleDocument sample) =>
            IsBSampleRequested(sample) || IsTransferCollection(sample);

        public static bool AnyBSampleRequested(HiwuManifestDocument document) =>
            document.Samples.Any(IsBSampleRequested);

        public static string? ReadSampleRef(HiwuSampleDocument sample)
        {
            if (!sample.Fields.TryGetValue("sampleRef", out var value))
                return null;
            value = value.Trim();
            return value.Length == 0 ? null : value;
        }

        /// Preferred order, then any other non-empty keys present on the mission.
        public static IReadOnlyList<string> MissionColumns(HiwuManifestDocument document) =>
            OrderedPresent(MissionFields, document.Mission.Keys.Where(key =>
                document.Mission.TryGetValue(key, out var value) && value.Length > 0));

        /// Preferred order, then any other non-empty keys present on any sample.
        public static IReadOnlyList<string> SampleColumns(HiwuManifestDocument document)
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sample in document.Samples)
            {
                foreach (var pair in sample.Fields)
                {
                    if (pair.Value.Length > 0)
                        present.Add(pair.Key);
                }
            }

            return OrderedPresent(SampleFields, present);
        }

        public List<HiwuFieldCompare> CompareMission(HiwuManifestDocument original)
        {
            return CompareFields(FieldOrder(MissionFields, Mission, original.Mission), Mission, original.Mission);
        }

        public List<HiwuSampleCompare> CompareSamples(HiwuManifestDocument original)
        {
            var rows = new List<HiwuSampleCompare>();
            foreach (var sample in Samples)
            {
                var match = original.Samples.FirstOrDefault(s =>
                    string.Equals(s.SampleId, sample.SampleId, StringComparison.Ordinal));
                rows.Add(new HiwuSampleCompare
                {
                    SampleId = sample.SampleId,
                    MissingOnOriginal = match == null,
                    Fields = CompareFields(
                        FieldOrder(SampleFields, sample.Fields, match?.Fields),
                        sample.Fields,
                        match?.Fields)
                });
            }

            return rows;
        }

        private static IReadOnlyList<string> OrderedPresent(IReadOnlyList<string> preferred, IEnumerable<string> presentKeys)
        {
            var present = new HashSet<string>(presentKeys, StringComparer.Ordinal);
            var columns = new List<string>(present.Count);
            foreach (var field in preferred)
            {
                if (present.Remove(field))
                    columns.Add(field);
            }

            foreach (var field in present.OrderBy(key => key, StringComparer.Ordinal))
                columns.Add(field);
            return columns;
        }

        private static IEnumerable<string> FieldOrder(
            IReadOnlyList<string> preferred,
            Dictionary<string, string> left,
            Dictionary<string, string>? right)
        {
            var present = new HashSet<string>(left.Keys, StringComparer.Ordinal);
            if (right != null)
            {
                foreach (var key in right.Keys)
                    present.Add(key);
            }

            return OrderedPresent(preferred, present);
        }

        private static List<HiwuFieldCompare> CompareFields(
            IEnumerable<string> order,
            Dictionary<string, string> amendment,
            Dictionary<string, string>? original)
        {
            var rows = new List<HiwuFieldCompare>();
            foreach (var field in order)
            {
                amendment.TryGetValue(field, out var newValue);
                var oldValue = "";
                original?.TryGetValue(field, out oldValue);
                if (string.IsNullOrEmpty(newValue) && string.IsNullOrEmpty(oldValue))
                    continue;

                var newDisplay = Display(field, newValue);
                var oldDisplay = Display(field, oldValue);
                rows.Add(new HiwuFieldCompare
                {
                    Field = field,
                    Original = oldDisplay,
                    Amendment = newDisplay,
                    Changed = !string.Equals(oldDisplay, newDisplay, StringComparison.Ordinal)
                });
            }

            return rows;
        }

        private static HiwuSampleDocument ReadSample(XElement sample)
        {
            var document = new HiwuSampleDocument();
            foreach (var child in sample.Elements())
            {
                var value = child.Value.Trim();
                document.Fields[child.Name.LocalName] = value;
                if (child.Name.LocalName == "sampleId")
                    document.SampleId = value;
            }

            return document;
        }
    }
}
