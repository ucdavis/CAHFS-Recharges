namespace CAHFS_Recharges.Models.Lockbox
{
    public class LockboxFileListItem
    {
        public Guid FileId { get; set; }
        public string RemoteFileName { get; set; } = "";
        public DateOnly FileBusinessDate { get; set; }
        public DateTime IngestedAtUtc { get; set; }
        public string ParseStatus { get; set; } = "";
        public DateTime? ParsedAtUtc { get; set; }
        public string? ParseError { get; set; }
        public int StagingRowCount { get; set; }
        public int ValidCount { get; set; }
        public int WarnCount { get; set; }
        public int ErrorCount { get; set; }
        public decimal TotalCheckAmount { get; set; }
    }
}
