namespace CAHFS_Recharges.Hiwu
{
    public class HiwuCoflSampleRow
    {
        public Guid FileId { get; set; }
        public string SampleKey { get; set; } = "";
        public long? AccessionId { get; set; }
        public long? AnimalId { get; set; }
        public long? SpecimenId { get; set; }
        public string Status { get; set; } = HiwuCoflSampleStatus.NotStarted;
        public string? ErrorMessage { get; set; }
        public DateTime UpdatedUtc { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? AccessionNumber { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? AnimalLabel { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? SpecimenLabel { get; set; }

        public static string Display(string? name, long? id)
        {
            if (string.IsNullOrWhiteSpace(name))
                return id?.ToString() ?? "—";
            return id == null ? name : name + " (" + id + ")";
        }
    }

    public static class HiwuCoflSampleStatus
    {
        public const string NotStarted = "Not started";
        public const string Partial = "Partial";
        public const string Imported = "Imported";
        public const string Failed = "Failed";
        public const string NotSent = "Not sent";

        public static string BadgeClass(string status) => status switch
        {
            Failed => "badge-soft-danger",
            Partial or NotSent => "badge-soft-warning",
            _ => "badge-soft-success"
        };

        public static string ForFile(string parseStatus, bool isAmendment, IReadOnlyList<HiwuCoflSampleRow> rows)
        {
            if (isAmendment || parseStatus == HiwuParseStatus.ParseError)
                return NotSent;
            if (rows.Count == 0)
                return NotStarted;
            if (rows.All(row => row.Status == Imported))
                return Imported;
            if (rows.Any(row => row.AccessionId != null || row.Status == Imported))
                return Partial;
            return Failed;
        }
    }
}
