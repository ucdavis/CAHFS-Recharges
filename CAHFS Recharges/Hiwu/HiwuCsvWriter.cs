using System.Text;

namespace CAHFS_Recharges.Hiwu
{
    public static class HiwuCsvWriter
    {
        public static readonly string[] Columns =
        [
            "location", "trackingRef", "courier", "dateShipped", "containerLockNumber",
            "sampledDate", "sampleId", "collectionPoint", "sampleType", "longTermStorage",
            "numberOfContainers", "expeditedAnalysis", "age", "gender", "lasix", "externalRef",
            "BSampleTestRequested", "duplicateTCO2SampleRequested"
        ];

        public static string Write(HiwuManifestDocument document)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", Columns));
            foreach (var sample in document.Samples)
            {
                var cells = Columns.Select(column => Quote(Value(document, sample, column)));
                builder.AppendLine(string.Join(",", cells));
            }

            return builder.ToString();
        }

        public static string DownloadName(string remoteFileName)
        {
            var name = Path.GetFileName(remoteFileName);
            var extension = Path.GetExtension(name);
            if (extension.Length > 0)
                name = name[..^extension.Length];
            return name + ".csv";
        }

        private static string Value(HiwuManifestDocument document, HiwuSampleDocument sample, string column)
        {
            string raw;
            if (sample.Fields.TryGetValue(column, out var sampleValue))
                raw = sampleValue;
            else if (document.Mission.TryGetValue(column, out var missionValue))
                raw = missionValue;
            else
                raw = "";

            if (string.Equals(column, "sampleId", StringComparison.Ordinal)
                && sample.Fields.TryGetValue("BSampleTestRequested", out var bSample)
                && string.Equals(
                    HiwuManifestDocument.Display("BSampleTestRequested", bSample),
                    "Yes",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(raw)
                    ? "[B-SAMPLE REQUIRED]"
                    : raw + " [B-SAMPLE REQUIRED]";
            }

            return raw;
        }

        private static string Quote(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
