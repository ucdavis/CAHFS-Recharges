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
        public static readonly string[] MissionFields =
        [
            "location", "trackingRef", "courier", "dateShipped", "containerLockNumber", "targetAnalytes"
        ];

        public static readonly string[] SampleFields =
        [
            "sampledDate", "sampleId", "collectionPoint", "sampleType", "age", "gender", "lasix",
            "longTermStorage", "externalRef", "numberOfContainers", "optionalComment", "expeditedAnalysis",
            "BSampleTestRequested", "BSampleTestNotes", "duplicateTCO2SampleRequested",
            "hairType", "hairCollectionMethod", "segmentalAnalysisRequested"
        ];

        public static readonly string[] BooleanFields =
        [
            "lasix", "longTermStorage", "expeditedAnalysis", "BSampleTestRequested",
            "duplicateTCO2SampleRequested", "segmentalAnalysisRequested"
        ];

        public static readonly string[] OptionalSampleFields =
        [
            "optionalComment", "BSampleTestNotes", "hairType", "hairCollectionMethod", "segmentalAnalysisRequested"
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

        public List<HiwuFieldCompare> CompareMission(HiwuManifestDocument original)
        {
            return CompareFields(MissionFields, Mission, original.Mission);
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
                    Fields = CompareFields(SampleFields, sample.Fields, match?.Fields)
                });
            }

            return rows;
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
