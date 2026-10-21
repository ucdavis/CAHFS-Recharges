namespace CAHFS_Recharges.Hiwu
{
    public class HiwuReportFile
    {
        public Guid FileId { get; set; }

        public long AccessionId { get; set; }

        public string Status { get; set; } = HiwuReportStatus.ResultsErrored;

        public string RawJson { get; set; } = "";

        public string? ReportXml { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }

    public static class HiwuReportStatus
    {
        public const string ResultsReady = "Results ready";

        public const string ResultsSent = "Results sent";

        public const string ResultsAccepted = "Results accepted";

        public const string ResultsErrored = "Results errored";

        public static string BadgeClass(string? status) => status switch
        {
            ResultsErrored => "badge-soft-danger",
            ResultsReady or ResultsSent or ResultsAccepted => "badge-soft-success",
            _ => "badge-soft-warning"
        };
    }
}
