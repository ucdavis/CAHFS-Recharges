namespace CAHFS_Recharges.Hiwu
{
    public class HiwuIngestRun
    {
        public Guid IngestRunId { get; set; }

        public DateTime StartedUtc { get; set; }

        public DateTime? CompletedUtc { get; set; }

        public string Status { get; set; } = "";

        public int FilesStored { get; set; }

        public int FilesDuplicate { get; set; }

        public int FilesFailed { get; set; }

        public string? ErrorSummary { get; set; }
    }
}
