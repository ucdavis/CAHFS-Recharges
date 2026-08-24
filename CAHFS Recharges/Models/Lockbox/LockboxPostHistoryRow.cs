namespace CAHFS_Recharges.Models.Lockbox
{
    public class LockboxPostHistoryRow
    {
        public int StagingId { get; set; }
        public DateTime? PostedAt { get; set; }
        public string PostStatus { get; set; } = "";
        public string? BillingId { get; set; }
        public string CheckNumber { get; set; } = "";
        public decimal CheckAmount { get; set; }
        public DateTime DepositDate { get; set; }
        public string BatchNumber { get; set; } = "";
        public string FileName { get; set; } = "";
        public string? ExternalDocNumber { get; set; }
        public string? PostError { get; set; }
    }

    public class LockboxPostHistorySummary
    {
        public int PostedCount { get; set; }
        public int FailedCount { get; set; }
        public decimal PostedAmount { get; set; }
    }
}
