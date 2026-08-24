namespace CAHFS_Recharges.Models.Lockbox
{
    public class LockboxStagingSummary
    {
        public int TotalCount { get; set; }
        public int ValidCount { get; set; }
        public int WarnCount { get; set; }
        public int ErrorCount { get; set; }
        public int PendingCount { get; set; }
        public int InvalidCustomerCount { get; set; }
        public decimal TotalCheckAmount { get; set; }
    }
}
