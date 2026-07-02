namespace CAHFS_Recharges.Models.Lockbox
{
    public static class LockboxIngestRunStatus
    {
        public const string Success = "Success";
        public const string NoFiles = "NoFiles";
        public const string Failed = "Failed";
    }

    public class LockboxIngestRun
    {
        public Guid IngestRunId { get; set; }
        public string LockboxId { get; set; } = null!;
        public DateOnly TargetBusinessDate { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string Status { get; set; } = null!;
        public int FilesFound { get; set; }
        public int FilesDownloaded { get; set; }
        public int FilesSkipped { get; set; }
        public string? ErrorSummary { get; set; }

        public ICollection<LockboxFile> Files { get; set; } = new List<LockboxFile>();
    }
}
