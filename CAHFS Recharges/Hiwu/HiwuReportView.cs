using System.Xml.Linq;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuReportSampleView
    {
        public string SampleId { get; init; } = "";
        public string ReceivedDate { get; init; } = "";
        public string ReportedDate { get; init; } = "";
        public string OverallResult { get; init; } = "";
        public string? Comment { get; init; }
        public string? Tco2Value { get; init; }
        public string? AdverseFindings { get; init; }
    }

    public sealed class HiwuReportView
    {
        public string Status { get; init; } = "";
        public string? ErrorMessage { get; init; }
        public string? ResultsRef { get; init; }
        public IReadOnlyList<HiwuReportSampleView> Samples { get; init; } = [];

        public static HiwuReportView Read(HiwuReportFile row)
        {
            var samples = new List<HiwuReportSampleView>();
            string? resultsRef = null;
            if (!string.IsNullOrWhiteSpace(row.ReportXml))
            {
                var document = XDocument.Parse(row.ReportXml);
                resultsRef = document.Root?.Element("resultsRef")?.Value;
                foreach (var sample in document.Descendants("sampleResult"))
                {
                    var findings = sample.Descendants("adverseFinding")
                        .Select(finding =>
                        {
                            var substance = finding.Element("substance")?.Value ?? "";
                            var level = finding.Element("concentrationLevel")?.Value ?? "";
                            return substance + " " + level;
                        })
                        .Where(text => !string.IsNullOrWhiteSpace(text));
                    samples.Add(new HiwuReportSampleView
                    {
                        SampleId = sample.Element("sampleId")?.Value ?? "",
                        ReceivedDate = sample.Element("receivedDate")?.Value ?? "",
                        ReportedDate = sample.Element("reportedDate")?.Value ?? "",
                        OverallResult = sample.Element("overallResult")?.Value ?? "",
                        Comment = sample.Element("optionalComment")?.Value,
                        Tco2Value = sample.Element("tco2Value")?.Value,
                        AdverseFindings = findings.Any() ? string.Join("; ", findings) : null
                    });
                }
            }

            return new HiwuReportView
            {
                Status = row.Status,
                ErrorMessage = row.ErrorMessage,
                ResultsRef = resultsRef,
                Samples = samples
            };
        }
    }
}