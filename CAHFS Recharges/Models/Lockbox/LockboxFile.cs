namespace CAHFS_Recharges.Models.Lockbox
{
    public class LockboxFile
    {
        public Guid FileId { get; set; }
        public string LockboxId { get; set; } = null!;
        public string RemoteFileName { get; set; } = null!;
        public DateOnly FileBusinessDate { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTime RemoteLastModifiedUtc { get; set; }
        public string ContentHash { get; set; } = null!;
        public byte[] RawContent { get; set; } = null!;
        public DateTime IngestedAtUtc { get; set; }
        public Guid IngestRunId { get; set; }

        public string ParseStatus { get; set; } = LockboxParseStatus.Pending;
        public DateTime? ParsedAtUtc { get; set; }
        public string? ParseError { get; set; }

        public LockboxIngestRun? IngestRun { get; set; }
    }
}
